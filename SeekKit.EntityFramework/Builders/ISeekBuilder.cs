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
    /// Adds an ascending sort column to the keyset. The order in which columns are added
    /// determines sort priority. Always add a unique column (e.g. <c>Id</c>) last.
    /// </summary>
    /// <typeparam name="TKey">The column's value type.</typeparam>
    /// <param name="keySelector">Expression selecting the column.</param>
    ISeekBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector);

    /// <summary>
    /// Adds a descending sort column to the keyset. The order in which columns are added
    /// determines sort priority. Always add a unique column (e.g. <c>Id</c>) last.
    /// </summary>
    /// <typeparam name="TKey">The column's value type.</typeparam>
    /// <param name="keySelector">Expression selecting the column.</param>
    ISeekBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector);

    /// <summary>
    /// Defers a join/projection until after ordering, keyset filtering, and the
    /// look-ahead <c>Take</c> have been applied — so <paramref name="transformer"/>
    /// only runs against the already-limited row set instead of the full table.
    /// The projected <typeparamref name="TResult"/> must expose public properties with
    /// the same names <b>and the same CLR type</b> as the sort columns registered via
    /// <see cref="OrderBy{TKey}"/> / <see cref="OrderByDescending{TKey}"/>, so SeekKit can
    /// read cursor values from the projected shape. A same-named property of a different
    /// type (e.g. sorting by an <c>int Id</c> on the entity but exposing a <c>long Id</c>
    /// on <typeparamref name="TResult"/>) is not validated up front and can throw or
    /// silently mis-encode the cursor at fetch time — keep the property type identical.
    /// </summary>
    /// <typeparam name="TResult">The projected result type.</typeparam>
    /// <param name="transformer">Projects the ordered, filtered, limited query to <typeparamref name="TResult"/>.</param>
    ISeekBuilder<T, TResult> Select<TResult>(Func<IQueryable<T>, IQueryable<TResult>> transformer);

    /// <summary>
    /// Executes the paginated query and returns a <see cref="SeekResult{T}"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask<SeekResult<T>> ToSeekResultAsync(CancellationToken cancellationToken = default);
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
}
