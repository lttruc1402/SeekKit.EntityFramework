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
    /// Overrides the keyset filter strategy. The default is
    /// <see cref="OrLogicSeekStrategy"/>, which the MongoDB LINQ provider
    /// translates to an indexable <c>$or</c> filter.
    /// <para>
    /// Supported only on the <see cref="IQueryable{T}"/> builder. The
    /// aggregation-pipeline and find builders always use the OR-logic keyset
    /// filter and throw <see cref="NotSupportedException"/> here.
    /// </para>
    /// </summary>
    ISeekMongoBuilder<T> WithStrategy(ISeekFilterStrategy strategy);

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
