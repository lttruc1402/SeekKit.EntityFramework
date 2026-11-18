namespace SeekKit.MongoDB.Builders;

/// <summary>
/// MongoDB implementation of the SeekKit builder. The keyset algorithm lives in
/// <see cref="SeekBuilderBase{T}"/> (SeekKit.Core); this class contributes the
/// Mongo-specific strategy default and async materialization through the
/// driver's LINQ provider.
/// </summary>
internal sealed class SeekMongoBuilder<T> : SeekBuilderBase<T>, ISeekMongoQueryableBuilder<T>
{
    private readonly SeekKitMongoOptions _options;

    public SeekMongoBuilder(IQueryable<T> query, ISeekSerializer serializer, ISeekValueConverter valueConverter, SeekKitMongoOptions options)
        : base(query, serializer, valueConverter, options)
    {
        _options = options;
    }

    public ISeekMongoQueryableBuilder<T> WithRequest(SeekRequest request)
    {
        SetRequest(request);
        return this;
    }

    ISeekMongoBuilder<T> ISeekMongoBuilder<T>.WithRequest(SeekRequest request) => WithRequest(request);

    public ISeekMongoQueryableBuilder<T> WithStrategy(ISeekFilterStrategy strategy)
    {
        SetStrategy(strategy);
        return this;
    }

    public ISeekMongoQueryableBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName = null)
    {
        AddSortColumn(keySelector, isDescending: false, resultPropertyName);
        return this;
    }

    ISeekMongoBuilder<T> ISeekMongoBuilder<T>.OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName) => OrderBy(keySelector, resultPropertyName);

    public ISeekMongoQueryableBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName = null)
    {
        AddSortColumn(keySelector, isDescending: true, resultPropertyName);
        return this;
    }

    ISeekMongoBuilder<T> ISeekMongoBuilder<T>.OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName) => OrderByDescending(keySelector, resultPropertyName);

    public ISeekMongoBuilder<T, TResult> Select<TResult>(Func<IQueryable<T>, IQueryable<TResult>> transformer)
    {
        return new SeekMongoProjectionBuilder<T, TResult>(
            Query, SortColumns.ToArray(), ResolveStrategy(), transformer, Serializer, ValueConverter, Request, _options, IncludeTotalCount);
    }

    public ValueTask<SeekResult<T>> ToSeekResultAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(cancellationToken);

    public ISeekMongoQueryableBuilder<T> WithTotalCount(bool include = true)
    {
        SetIncludeTotalCount(include);
        return this;
    }

    ISeekMongoBuilder<T> ISeekMongoBuilder<T>.WithTotalCount(bool include) => WithTotalCount(include);

    public IAsyncEnumerable<SeekResult<T>> ToPagesAsync(CancellationToken cancellationToken = default)
        => ExecutePagesAsync(cancellationToken);

    public IAsyncEnumerable<T> ToItemsAsync(CancellationToken cancellationToken = default)
        => ExecuteItemsAsync(cancellationToken);

    protected override Task<long> LongCountAsync(IQueryable<T> query, CancellationToken cancellationToken)
        => MongoCount.CountAsync(query, cancellationToken);

    protected override ISeekFilterStrategy ResolveDefaultStrategy()
        => new OrLogicSeekStrategy(ValueConverter);

    protected override Task<List<T>> MaterializeAsync(IQueryable<T> query, CancellationToken cancellationToken)
    {
        // Queries created from IMongoCollection<T>.AsQueryable() implement
        // IAsyncCursorSource<T>, giving true async server-side execution.
        if (query is IAsyncCursorSource<T> cursorSource)
            return cursorSource.ToListAsync(cancellationToken);

        // Fallback for non-Mongo providers (e.g. in-memory queryables in tests).
        return Task.FromResult(query.ToList());
    }
}
