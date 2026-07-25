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
    ISeekMongoBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector);

    /// <summary>
    /// Adds a descending sort column to the keyset. The order in which columns are added
    /// determines sort priority. Always add a unique column (e.g. <c>Id</c>) last.
    /// </summary>
    ISeekMongoBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector);

    /// <summary>
    /// Executes the paginated query and returns a <see cref="SeekResult{T}"/>.
    /// </summary>
    ValueTask<SeekResult<T>> ToSeekResultAsync(CancellationToken cancellationToken = default);
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

    /// <summary>Adds an ascending sort column to the keyset.</summary>
    new ISeekMongoQueryableBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector);

    /// <summary>Adds a descending sort column to the keyset.</summary>
    new ISeekMongoQueryableBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector);

    /// <summary>
    /// Defers a projection until after ordering, keyset filtering, and the look-ahead
    /// limit have been applied — so <paramref name="transformer"/> only runs against the
    /// already-limited row set instead of the full collection. The projected
    /// <typeparamref name="TResult"/> must expose public properties with the same names
    /// and CLR types as the sort columns registered via <see cref="OrderBy{TKey}"/> /
    /// <see cref="OrderByDescending{TKey}"/>.
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

    /// <summary>Adds an ascending sort column to the keyset.</summary>
    new ISeekMongoAggregateBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector);

    /// <summary>Adds a descending sort column to the keyset.</summary>
    new ISeekMongoAggregateBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector);

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

    /// <summary>Adds an ascending sort column to the keyset.</summary>
    new ISeekMongoFindBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector);

    /// <summary>Adds a descending sort column to the keyset.</summary>
    new ISeekMongoFindBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector);

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
}
