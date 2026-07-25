namespace SeekKit.MongoDB.Builders;

/// <summary>
/// MongoDB implementation of a projected SeekKit builder over an
/// <see cref="IQueryable{T}"/>. The projection push-down logic lives in
/// <see cref="SeekProjectionBuilderBase{T, TResult}"/> (SeekKit.Core); this class only
/// contributes Mongo-specific async materialization.
/// </summary>
internal sealed class SeekMongoProjectionBuilder<T, TResult>
    : SeekProjectionBuilderBase<T, TResult>, ISeekMongoBuilder<T, TResult>
{
    public SeekMongoProjectionBuilder(
        IQueryable<T> query,
        IReadOnlyList<ISortColumn<T>> sortColumns,
        ISeekFilterStrategy filterStrategy,
        Func<IQueryable<T>, IQueryable<TResult>> transformer,
        ISeekSerializer serializer,
        ISeekValueConverter valueConverter,
        SeekRequest? request,
        SeekOptionsBase options)
        : base(query, sortColumns, filterStrategy, transformer, serializer, valueConverter, request, options)
    {
    }

    protected override Task<List<TResult>> MaterializeAsync(IQueryable<TResult> query, CancellationToken cancellationToken)
    {
        if (query is IAsyncCursorSource<TResult> cursorSource)
            return cursorSource.ToListAsync(cancellationToken);

        return Task.FromResult(query.ToList());
    }

    public ValueTask<SeekResult<TResult>> ToSeekResultAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(cancellationToken);
}
