namespace SeekKit.MongoDB.Builders;

/// <summary>
/// Fluent builder for constructing and executing a cursor-paginated MongoDB query.
/// Obtain an instance via <see cref="ISeekMongoService.CreateBuilder{T}(IMongoCollection{T})"/>.
/// </summary>
/// <typeparam name="T">The document type being queried.</typeparam>
public interface ISeekMongoBuilder<T>
{
    /// <summary>
    /// Sets the pagination request (token + page size) for this query.
    /// </summary>
    ISeekMongoBuilder<T> WithRequest(SeekRequest request);

    /// <summary>
    /// Adds an ascending sort column to the keyset. The order in which columns are added
    /// determines sort priority. Always add a unique column (e.g. <c>Id</c>) last.
    /// </summary>
    /// <param name="keySelector">
    /// Expression selecting the field. May be nullable. A computed expression
    /// (<c>x =&gt; x.Name.ToLower()</c>) is supported on the queryable origin only — the
    /// aggregate and find origins can sort by fields alone.
    /// </param>
    /// <param name="resultPropertyName">
    /// Optional escape hatch for queries later projected via <c>Select()</c>: names the
    /// property on the projected result that holds this column's value. On the queryable
    /// origin you can normally leave it out — SeekKit reads the same-named property, or
    /// traces the value through the LINQ projection (renamed members, joins, identity
    /// selectors such as <c>x =&gt; x</c>). The aggregate and find origins build their
    /// pipelines outside LINQ, so there it is required whenever the projected result has
    /// no same-named property — always the case for an identity selector.
    /// </param>
    ISeekMongoBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName = null);

    /// <summary>
    /// Adds a descending sort column to the keyset. The order in which columns are added
    /// determines sort priority. Always add a unique column (e.g. <c>Id</c>) last.
    /// </summary>
    /// <param name="keySelector">
    /// Expression selecting the field. May be nullable. A computed expression
    /// (<c>x =&gt; x.Name.ToLower()</c>) is supported on the queryable origin only — the
    /// aggregate and find origins can sort by fields alone.
    /// </param>
    /// <param name="resultPropertyName">
    /// Optional escape hatch for queries later projected via <c>Select()</c>: names the
    /// property on the projected result that holds this column's value. On the queryable
    /// origin you can normally leave it out — SeekKit reads the same-named property, or
    /// traces the value through the LINQ projection (renamed members, joins, identity
    /// selectors such as <c>x =&gt; x</c>). The aggregate and find origins build their
    /// pipelines outside LINQ, so there it is required whenever the projected result has
    /// no same-named property — always the case for an identity selector.
    /// </param>
    ISeekMongoBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName = null);

    /// <summary>
    /// Also returns the total number of documents the query matches across all pages, as
    /// <see cref="SeekResult{T}.TotalCount"/>. Costs one extra count round trip per call,
    /// over the whole unpaged query — so it is off by default. A common pattern is to ask
    /// for it on the first page only: <c>.WithTotalCount(request.Token is null)</c>.
    /// </summary>
    /// <param name="include"><see langword="false"/> to turn it back off.</param>
    ISeekMongoBuilder<T> WithTotalCount(bool include = true);

    /// <summary>
    /// Executes the paginated query and returns a <see cref="SeekResult{T}"/>.
    /// </summary>
    ValueTask<SeekResult<T>> ToSeekResultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks the whole result page by page: yields the requested page (the first one when
    /// the request has no token), then follows <see cref="SeekResult{T}.NextToken"/> until
    /// the end. Each page is one keyset query of <see cref="SeekRequest.PageSize"/>
    /// documents, fetched only when the enumeration reaches it — memory stays flat however
    /// large the collection is. Meant for exports, batch jobs, and re-indexing.
    /// </summary>
    /// <remarks>
    /// With <see cref="WithTotalCount"/> the count runs once, before the first page, and
    /// is repeated on every page. Don't use the builder for anything else while the
    /// enumeration is in progress.
    /// </remarks>
    IAsyncEnumerable<SeekResult<T>> ToPagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Like <see cref="ToPagesAsync"/>, but yields the individual documents:
    /// <c>await foreach (var doc in builder.ToItemsAsync(ct))</c>.
    /// </summary>
    IAsyncEnumerable<T> ToItemsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Fluent builder for a cursor-paginated query over an <see cref="IQueryable{T}"/>
/// (from <see cref="IMongoCollection{T}"/> or a pre-filtered queryable). Obtain an
/// instance via <see cref="ISeekMongoService.CreateBuilder{T}(IMongoCollection{T})"/> or
/// <see cref="ISeekMongoService.CreateBuilder{T}(IQueryable{T})"/>.
/// </summary>
/// <typeparam name="T">The document type being queried.</typeparam>
public interface ISeekMongoQueryableBuilder<T> : ISeekMongoBuilder<T>
{
    /// <summary>Sets the pagination request (token + page size) for this query.</summary>
    new ISeekMongoQueryableBuilder<T> WithRequest(SeekRequest request);

    /// <summary>
    /// Overrides the keyset filter strategy. The default is
    /// <see cref="OrLogicSeekStrategy"/>, which the MongoDB LINQ provider
    /// translates to an indexable <c>$or</c> filter.
    /// </summary>
    ISeekMongoQueryableBuilder<T> WithStrategy(ISeekFilterStrategy strategy);

    /// <summary>Also returns the total number of matching documents (see <see cref="ISeekMongoBuilder{T}.WithTotalCount"/>).</summary>
    new ISeekMongoQueryableBuilder<T> WithTotalCount(bool include = true);

    /// <summary>Adds an ascending sort column to the keyset.</summary>
    new ISeekMongoQueryableBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName = null);

    /// <summary>Adds a descending sort column to the keyset.</summary>
    new ISeekMongoQueryableBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName = null);

    /// <summary>
    /// Defers a projection until after ordering, keyset filtering, and the look-ahead
    /// limit have been applied — so <paramref name="transformer"/> only runs against the
    /// already-limited row set instead of the full collection. Cursor values are read
    /// back from <typeparamref name="TResult"/>: from the public property named like the
    /// sort column registered via <see cref="OrderBy{TKey}"/> /
    /// <see cref="OrderByDescending{TKey}"/> (keep its CLR type identical), or, when there
    /// is none, from the property the value is traced to through
    /// <paramref name="transformer"/>'s expression tree.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="transformer">Projects the ordered, filtered, limited query to <typeparamref name="TResult"/>.</param>
    ISeekMongoBuilder<T, TResult> Select<TResult>(Func<IQueryable<T>, IQueryable<TResult>> transformer);
}

/// <summary>
/// Fluent builder for a cursor-paginated query over an <see cref="IAggregateFluent{T}"/>
/// pipeline. Obtain an instance via
/// <see cref="ISeekMongoService.CreateBuilder{T}(IAggregateFluent{T})"/>.
/// </summary>
/// <typeparam name="T">The pipeline's output document type.</typeparam>
public interface ISeekMongoAggregateBuilder<T> : ISeekMongoBuilder<T>
{
    /// <summary>Sets the pagination request (token + page size) for this query.</summary>
    new ISeekMongoAggregateBuilder<T> WithRequest(SeekRequest request);

    /// <summary>Also returns the total number of matching documents (see <see cref="ISeekMongoBuilder{T}.WithTotalCount"/>).</summary>
    new ISeekMongoAggregateBuilder<T> WithTotalCount(bool include = true);

    /// <summary>Adds an ascending sort column to the keyset.</summary>
    new ISeekMongoAggregateBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName = null);

    /// <summary>Adds a descending sort column to the keyset.</summary>
    new ISeekMongoAggregateBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName = null);

    /// <summary>
    /// Defers a projection until after the keyset <c>$match</c>, <c>$sort</c>, and
    /// <c>$limit</c> stages — so <paramref name="transformer"/> (e.g. a <c>$lookup</c> or
    /// <c>$project</c>) only runs against the already-limited row set. The projected
    /// <typeparamref name="TResult"/> must expose public properties with the same names
    /// and CLR types as the sort columns registered via <see cref="OrderBy{TKey}"/> /
    /// <see cref="OrderByDescending{TKey}"/>.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="transformer">Projects the ordered, filtered, limited pipeline to <typeparamref name="TResult"/>.</param>
    ISeekMongoBuilder<T, TResult> Select<TResult>(Func<IAggregateFluent<T>, IAggregateFluent<TResult>> transformer);
}

/// <summary>
/// Fluent builder for a cursor-paginated query over an <see cref="IFindFluent{T, T}"/>.
/// Obtain an instance via <see cref="ISeekMongoService.CreateBuilder{T}(IFindFluent{T, T})"/>.
/// </summary>
/// <typeparam name="T">The document type being queried.</typeparam>
public interface ISeekMongoFindBuilder<T> : ISeekMongoBuilder<T>
{
    /// <summary>Sets the pagination request (token + page size) for this query.</summary>
    new ISeekMongoFindBuilder<T> WithRequest(SeekRequest request);

    /// <summary>Also returns the total number of matching documents (see <see cref="ISeekMongoBuilder{T}.WithTotalCount"/>).</summary>
    new ISeekMongoFindBuilder<T> WithTotalCount(bool include = true);

    /// <summary>Adds an ascending sort column to the keyset.</summary>
    new ISeekMongoFindBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName = null);

    /// <summary>Adds a descending sort column to the keyset.</summary>
    new ISeekMongoFindBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName = null);

    /// <summary>
    /// Defers a projection until after the keyset filter, sort, and limit have been
    /// applied — so <paramref name="transformer"/> (via <c>IFindFluent{T,T}.Project</c>)
    /// only runs against the already-limited row set. The projected
    /// <typeparamref name="TResult"/> must expose public properties with the same names
    /// and CLR types as the sort columns registered via <see cref="OrderBy{TKey}"/> /
    /// <see cref="OrderByDescending{TKey}"/>.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="transformer">Projects the ordered, filtered, limited find query to <typeparamref name="TResult"/>.</param>
    ISeekMongoBuilder<T, TResult> Select<TResult>(Func<IFindFluent<T, T>, IFindFluent<T, TResult>> transformer);
}

/// <summary>
/// Fluent builder for executing a projected cursor-paginated MongoDB query. Obtain an
/// instance via <see cref="ISeekMongoQueryableBuilder{T}.Select{TResult}"/>,
/// <see cref="ISeekMongoAggregateBuilder{T}.Select{TResult}"/>, or
/// <see cref="ISeekMongoFindBuilder{T}.Select{TResult}"/>.
/// </summary>
/// <typeparam name="T">The document type the query, ordering, and keyset filter operate on.</typeparam>
/// <typeparam name="TResult">The projected result type returned to the caller.</typeparam>
public interface ISeekMongoBuilder<T, TResult>
{
    /// <summary>Executes the paginated, projected query and returns a <see cref="SeekResult{TResult}"/>.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<SeekResult<TResult>> ToSeekResultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Walks the whole projected result page by page, following
    /// <see cref="SeekResult{T}.NextToken"/> until the end. See
    /// <see cref="ISeekMongoBuilder{T}.ToPagesAsync"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<SeekResult<TResult>> ToPagesAsync(CancellationToken cancellationToken = default);

    /// <summary>Like <see cref="ToPagesAsync"/>, but yields the individual projected documents.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    IAsyncEnumerable<TResult> ToItemsAsync(CancellationToken cancellationToken = default);
}
