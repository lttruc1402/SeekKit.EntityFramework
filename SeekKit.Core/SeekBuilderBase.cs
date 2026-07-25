namespace SeekKit.Core;

/// <summary>
/// Base class for <see cref="IQueryable{T}"/>-backed SeekKit builders (EF Core,
/// MongoDB LINQ provider). Adds ordering, keyset filtering via an
/// <see cref="ISeekFilterStrategy"/>, and look-ahead <c>Take</c> on top of the
/// provider-agnostic algorithm in <see cref="SeekBuilderCore{T}"/>. Concrete
/// builders supply:
/// <list type="bullet">
///   <item><see cref="ResolveDefaultStrategy"/> — the filter strategy to use when none was set explicitly.</item>
///   <item><see cref="MaterializeAsync"/> — provider-specific async query execution.</item>
/// </list>
/// </summary>
/// <typeparam name="T">The entity/document type being paginated.</typeparam>
public abstract class SeekBuilderBase<T> : SeekBuilderCore<T>
{
    private readonly IQueryable<T> _query;
    private ISeekFilterStrategy? _filterStrategy;

    protected SeekBuilderBase(
        IQueryable<T> query,
        ISeekSerializer serializer,
        ISeekValueConverter valueConverter,
        SeekOptionsBase options)
        : base(serializer, valueConverter, options)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
    }

    /// <summary>The base queryable this builder was constructed with.</summary>
    protected IQueryable<T> Query => _query;

    /// <summary>Overrides the filter strategy for this query.</summary>
    protected void SetStrategy(ISeekFilterStrategy strategy)
    {
        _filterStrategy = strategy;
    }

    /// <summary>
    /// Returns the filter strategy to use when none was set explicitly
    /// (e.g. the strategy resolved from provider-specific options).
    /// </summary>
    protected abstract ISeekFilterStrategy ResolveDefaultStrategy();

    /// <summary>
    /// Returns the filter strategy for this builder, resolving and caching the
    /// default (via <see cref="ResolveDefaultStrategy"/>) if none was set explicitly.
    /// </summary>
    protected ISeekFilterStrategy ResolveStrategy()
    {
        _filterStrategy ??= ResolveDefaultStrategy();
        return _filterStrategy;
    }

    /// <summary>
    /// Executes <paramref name="query"/> asynchronously using the provider's
    /// LINQ execution API (e.g. EF Core's or the MongoDB driver's <c>ToListAsync</c>).
    /// </summary>
    protected abstract Task<List<T>> MaterializeAsync(IQueryable<T> query, CancellationToken cancellationToken);

    protected sealed override Task<List<T>> FetchAsync(
        SeekData? seekData,
        SeekDirection direction,
        int limit,
        CancellationToken cancellationToken)
    {
        IQueryable<T> query = _query.ApplyOrdering(SortColumns, direction);

        if (seekData is not null)
        {
            var strategy = ResolveStrategy();

            if (strategy is IPageSizeAware pageSizeFilter)
                pageSizeFilter.SetPageSize(limit);

            query = strategy.ApplyFilter(query, Expression.Parameter(typeof(T), "e"), SortColumns, seekData);
        }

        return MaterializeAsync(query.Take(limit), cancellationToken);
    }
}
