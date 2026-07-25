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
        SeekOptionsBase options)
        : base(query, sortColumns, filterStrategy, transformer, serializer, valueConverter, request, options)
    {
    }

    protected override Task<List<TResult>> MaterializeAsync(IQueryable<TResult> query, CancellationToken cancellationToken)
        => query.ToListAsync(cancellationToken);

    public ValueTask<SeekResult<TResult>> ToSeekResultAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(cancellationToken);
}
