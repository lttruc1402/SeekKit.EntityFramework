namespace SeekKit.MongoDB.Builders;

/// <summary>
/// Total-count queries for the three MongoDB query models. Each counts the query as the
/// caller supplied it — before any keyset filter, sort, or limit SeekKit adds.
/// </summary>
internal static class MongoCount
{
    /// <summary>Counts a LINQ queryable; falls back to a synchronous count for non-Mongo providers.</summary>
    public static Task<long> CountAsync<T>(IQueryable<T> query, CancellationToken cancellationToken)
        => query is IAsyncCursorSource<T>
            ? global::MongoDB.Driver.Linq.MongoQueryable.LongCountAsync(query, cancellationToken)
            : Task.FromResult(query.LongCount());

    /// <summary>Counts an aggregation pipeline by appending a <c>$count</c> stage.</summary>
    public static async Task<long> CountAsync<T>(IAggregateFluent<T> aggregate, CancellationToken cancellationToken)
    {
        // $count emits no document at all for an empty input.
        var result = await aggregate.Count().FirstOrDefaultAsync(cancellationToken);
        return result?.Count ?? 0;
    }

    /// <summary>
    /// Counts a find query under <paramref name="baseFilter"/>. <see cref="IFindFluent{T, TProjection}"/>
    /// is mutable and a previous page fetch leaves its keyset filter and limit behind, so
    /// both are reset for the count and the limit is put back afterwards.
    /// </summary>
    public static async Task<long> CountAsync<T, TProjection>(
        IFindFluent<T, TProjection> find,
        FilterDefinition<T> baseFilter,
        CancellationToken cancellationToken)
    {
        find.Filter = baseFilter;

        var limit = find.Options?.Limit;
        if (limit is not null)
            find.Limit(null);

        try
        {
            return await find.CountDocumentsAsync(cancellationToken);
        }
        finally
        {
            if (limit is not null)
                find.Limit(limit);
        }
    }
}
