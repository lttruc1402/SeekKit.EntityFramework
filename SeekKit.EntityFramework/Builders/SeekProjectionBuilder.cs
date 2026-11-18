namespace SeekKit.EntityFramework.Builders;

/// <summary>
/// EF Core implementation of a projected SeekKit builder. The pagination algorithm and
/// projection push-down logic live in <see cref="SeekProjectionBuilderBase{T, TResult}"/>
/// (SeekKit.Core); this class only contributes EF-specific async materialization.
/// </summary>
internal sealed class SeekProjectionBuilder<T, TResult>
    : SeekProjectionBuilderBase<T, TResult>, ISeekBuilder<T, TResult>
{
    public SeekProjectionBuilder(
        IQueryable<T> query,
        IReadOnlyList<ISortColumn<T>> sortColumns,
        ISeekFilterStrategy filterStrategy,
        Func<IQueryable<T>, IQueryable<TResult>> transformer,
        ISeekSerializer serializer,
        ISeekValueConverter valueConverter,
        SeekRequest? request,
        SeekOptionsBase options,
        bool includeTotalCount)
        : base(query, sortColumns, filterStrategy, transformer, serializer, valueConverter, request, options)
    {
        IncludeTotalCount = includeTotalCount;
    }

    protected override Task<List<TResult>> MaterializeAsync(IQueryable<TResult> query, CancellationToken cancellationToken)
        => query.ToListAsync(cancellationToken);

    protected override Task<long> LongCountAsync(IQueryable<T> query, CancellationToken cancellationToken)
        => query.LongCountAsync(cancellationToken);

    public ValueTask<SeekResult<TResult>> ToSeekResultAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(cancellationToken);

    public IAsyncEnumerable<SeekResult<TResult>> ToPagesAsync(CancellationToken cancellationToken = default)
        => ExecutePagesAsync(cancellationToken);

    public IAsyncEnumerable<TResult> ToItemsAsync(CancellationToken cancellationToken = default)
        => ExecuteItemsAsync(cancellationToken);
}
