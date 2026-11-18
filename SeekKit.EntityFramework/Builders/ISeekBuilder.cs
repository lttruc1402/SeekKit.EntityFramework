namespace SeekKit.EntityFramework.Builders;

/// <summary>
/// Fluent builder for constructing and executing a cursor-paginated query.
/// Obtain an instance via <see cref="ISeekService.CreateBuilder{T}(IQueryable{T})"/>.
/// </summary>
/// <typeparam name="T">The entity type being queried.</typeparam>
public interface ISeekBuilder<T>
{
    /// <summary>
    /// Sets the pagination request (token + page size) for this query.
    /// </summary>
    /// <param name="request">The client's pagination request.</param>
    ISeekBuilder<T> WithRequest(SeekRequest request);

    /// <summary>
    /// Overrides the query strategy resolved from <see cref="SeekKitOptions"/> with a custom
    /// <see cref="ISeekFilterStrategy"/> implementation.
    /// </summary>
    /// <param name="strategy">The custom strategy to use.</param>
    ISeekBuilder<T> WithStrategy(ISeekFilterStrategy strategy);

    /// <summary>
    /// Forces the PostgreSQL row-value (tuple) comparison strategy:
    /// <c>WHERE (a, b) &gt; (@a, @b)</c>. Fastest option on PostgreSQL but requires
    /// all order columns to sort in the same direction.
    /// </summary>
    ISeekBuilder<T> WithTupleComparison();

    /// <summary>
    /// Forces the OR-predicate expansion strategy.
    /// Most database-compatible option; suitable when mixed sort directions are present.
    /// </summary>
    ISeekBuilder<T> WithOrPredicate();

    /// <summary>
    /// Forces the UNION ALL strategy. Handles mixed sort directions and works across
    /// all supported database providers.
    /// </summary>
    ISeekBuilder<T> WithUnionAll();

    /// <summary>
    /// Also returns the total number of rows the query matches across all pages, as
    /// <see cref="SeekResult{T}.TotalCount"/>. Costs one extra <c>COUNT(*)</c> round trip
    /// per call, over the whole unpaged query — so it is off by default. A common pattern
    /// is to ask for it on the first page only:
    /// <c>.WithTotalCount(request.Token is null)</c>.
    /// </summary>
    /// <param name="include"><see langword="false"/> to turn it back off.</param>
    ISeekBuilder<T> WithTotalCount(bool include = true);

    /// <summary>
    /// Adds an ascending sort column to the keyset. The order in which columns are added
    /// determines sort priority. Always add a unique column (e.g. <c>Id</c>) last.
    /// </summary>
    /// <typeparam name="TKey">The column's value type.</typeparam>
    /// <param name="keySelector">
    /// Expression selecting the column: a property (<c>x =&gt; x.CreatedAt</c>), the element
    /// itself (<c>x =&gt; x</c>), or a computed expression EF Core can translate
    /// (<c>x =&gt; x.Name.ToLower()</c>). The column may be nullable.
    /// </param>
    /// <param name="resultPropertyName">
    /// Optional escape hatch for queries later projected via <see cref="Select{TResult}"/>:
    /// names the property on the projected <c>TResult</c> that holds this column's value.
    /// Normally leave it out — SeekKit reads the same-named property, or traces the value
    /// through the projection (renamed members, keys carried through joins, identity
    /// selectors such as <c>x =&gt; x</c>). Pass it only when the value can't be traced:
    /// it is computed, comes from the optional side of an outer join, or the projection
    /// isn't built from LINQ operators.
    /// </param>
    ISeekBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName = null);

    /// <summary>
    /// Adds a descending sort column to the keyset. The order in which columns are added
    /// determines sort priority. Always add a unique column (e.g. <c>Id</c>) last.
    /// </summary>
    /// <typeparam name="TKey">The column's value type.</typeparam>
    /// <param name="keySelector">
    /// Expression selecting the column: a property (<c>x =&gt; x.CreatedAt</c>), the element
    /// itself (<c>x =&gt; x</c>), or a computed expression EF Core can translate
    /// (<c>x =&gt; x.Name.ToLower()</c>). The column may be nullable.
    /// </param>
    /// <param name="resultPropertyName">
    /// Optional escape hatch for queries later projected via <see cref="Select{TResult}"/>:
    /// names the property on the projected <c>TResult</c> that holds this column's value.
    /// Normally leave it out — SeekKit reads the same-named property, or traces the value
    /// through the projection (renamed members, keys carried through joins, identity
    /// selectors such as <c>x =&gt; x</c>). Pass it only when the value can't be traced:
    /// it is computed, comes from the optional side of an outer join, or the projection
    /// isn't built from LINQ operators.
    /// </param>
    ISeekBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName = null);

    /// <summary>
    /// Defers a join/projection until after ordering, keyset filtering, and the
    /// look-ahead <c>Take</c> have been applied — so <paramref name="transformer"/>
    /// only runs against the already-limited row set instead of the full table.
    /// <para>
    /// Cursor values are read back from the projected <typeparamref name="TResult"/>. For
    /// each sort column registered via <see cref="OrderBy{TKey}"/> /
    /// <see cref="OrderByDescending{TKey}"/> SeekKit uses the same-named public property
    /// when <typeparamref name="TResult"/> has one; otherwise it traces the value through
    /// <paramref name="transformer"/>'s expression tree (<c>Select</c>, <c>Join</c>,
    /// <c>LeftJoin</c>, <c>GroupJoin</c> + <c>SelectMany</c>, …) to the property it ends up
    /// in. The transformer is invoked once against an empty in-memory queryable for this —
    /// no query runs. If the value can't be found either way (it is computed, or read from
    /// the optional side of an outer join) this method throws
    /// <see cref="InvalidOperationException"/>; name the property via the
    /// <c>resultPropertyName</c> argument of <c>OrderBy</c> / <c>OrderByDescending</c>.
    /// </para>
    /// <para>
    /// A same-named property of a different type (e.g. sorting by an <c>int Id</c> on the
    /// entity but exposing a <c>long Id</c> on <typeparamref name="TResult"/>) is not
    /// validated up front and can throw or silently mis-encode the cursor at fetch time —
    /// keep the property type identical.
    /// </para>
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="transformer">Projects the ordered, filtered, limited query to <typeparamref name="TResult"/>.</param>
    ISeekBuilder<T, TResult> Select<TResult>(Func<IQueryable<T>, IQueryable<TResult>> transformer);

    /// <summary>
    /// Executes the paginated query and returns a <see cref="SeekResult{T}"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<SeekResult<T>> ToSeekResultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks the whole result page by page: yields the requested page (the first one when
    /// the request has no token), then follows <see cref="SeekResult{T}.NextToken"/> until
    /// the end. Each page is one keyset query of <see cref="SeekRequest.PageSize"/> rows,
    /// fetched only when the enumeration reaches it — memory stays flat however large the
    /// table is. Meant for exports, batch jobs, and re-indexing.
    /// </summary>
    /// <remarks>
    /// With <see cref="WithTotalCount"/> the count runs once, before the first page, and
    /// is repeated on every page. Don't use the builder for anything else while the
    /// enumeration is in progress.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<SeekResult<T>> ToPagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="ToPagesAsync"/>, but yields the individual rows:
    /// <c>await foreach (var order in builder.ToItemsAsync(ct))</c>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<T> ToItemsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Fluent builder for executing a projected cursor-paginated query. Obtain an instance
/// via <see cref="ISeekBuilder{T}.Select{TResult}"/>.
/// </summary>
/// <typeparam name="T">The entity type the query, ordering, and keyset filter operate on.</typeparam>
/// <typeparam name="TResult">The projected result type returned to the caller.</typeparam>
public interface ISeekBuilder<T, TResult>
{
    /// <summary>
    /// Executes the paginated, projected query and returns a <see cref="SeekResult{TResult}"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<SeekResult<TResult>> ToSeekResultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks the whole projected result page by page, following
    /// <see cref="SeekResult{T}.NextToken"/> until the end. See
    /// <see cref="ISeekBuilder{T}.ToPagesAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<SeekResult<TResult>> ToPagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="ToPagesAsync"/>, but yields the individual projected rows.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<TResult> ToItemsAsync(CancellationToken cancellationToken = default);
}
