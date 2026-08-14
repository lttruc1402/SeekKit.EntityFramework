namespace SeekKit.Core;

/// <summary>
/// Base class for a projected (join/select push-down) keyset pagination builder.
/// Applies ordering, keyset filtering, and the look-ahead <c>Take</c> to
/// <typeparamref name="T"/> exactly like <see cref="SeekBuilderBase{T}"/>, then hands the
/// already-limited query to the transformer so joins/projections only run against the
/// limited row set instead of the whole table. Cursor values are read back from the
/// projected <typeparamref name="TResult"/> items via <see cref="Helpers.ResultKeyAccessor"/>,
/// matched by the same property-path names used when the sort columns were registered.
/// </summary>
/// <typeparam name="T">The entity type the query, ordering, and keyset filter operate on.</typeparam>
/// <typeparam name="TResult">The projected result type returned to the caller.</typeparam>
public abstract class SeekProjectionBuilderBase<T, TResult>
{
    private readonly IQueryable<T> _query;
    private readonly IReadOnlyList<ISortColumn<T>> _sortColumns;
    private readonly ISeekFilterStrategy _filterStrategy;
    private readonly Func<IQueryable<T>, IQueryable<TResult>> _transformer;
    private readonly ISeekSerializer _serializer;
    private readonly ISeekValueConverter _valueConverter;
    private readonly SeekOptionsBase _options;
    private readonly Func<TResult, object?>[] _resultAccessors;

    private SeekRequest? _request;

    protected SeekProjectionBuilderBase(
        IQueryable<T> query,
        IReadOnlyList<ISortColumn<T>> sortColumns,
        ISeekFilterStrategy filterStrategy,
        Func<IQueryable<T>, IQueryable<TResult>> transformer,
        ISeekSerializer serializer,
        ISeekValueConverter valueConverter,
        SeekRequest? request,
        SeekOptionsBase options)
    {
        if (sortColumns is null || sortColumns.Count == 0)
            throw new InvalidOperationException(
                "At least one order field is required. Use OrderBy() or OrderByDescending() before calling Select().");

        _query          = query ?? throw new ArgumentNullException(nameof(query));
        _sortColumns    = sortColumns;
        _filterStrategy = filterStrategy ?? throw new ArgumentNullException(nameof(filterStrategy));
        _transformer    = transformer ?? throw new ArgumentNullException(nameof(transformer));
        _serializer     = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _valueConverter = valueConverter ?? throw new ArgumentNullException(nameof(valueConverter));
        _options        = options ?? throw new ArgumentNullException(nameof(options));
        _request        = request;

        _resultAccessors = new Func<TResult, object?>[_sortColumns.Count];
        for (int i = 0; i < _sortColumns.Count; i++)
        {
            if (_sortColumns[i].PropertyPath == SeekIdentitySortColumn.PropertyPath)
                throw new InvalidOperationException(
                    "Cannot use Select() with a sort column added via an identity selector " +
                    "(x => x) — there's no property name to match against the projected " +
                    $"{typeof(TResult).Name} shape. Pass the matching TResult property name " +
                    "explicitly, e.g. OrderByDescending(x => x, resultPropertyName: \"Id\").");

            _resultAccessors[i] = Helpers.ResultKeyAccessor.GetAccessor<TResult>(_sortColumns[i].PropertyPath);
        }
    }

    /// <summary>
    /// Executes <paramref name="query"/> (already ordered, keyset-filtered, and
    /// transformed) using the provider's LINQ execution API.
    /// </summary>
    protected abstract Task<List<TResult>> MaterializeAsync(IQueryable<TResult> query, CancellationToken cancellationToken);

    /// <summary>
    /// Runs the full keyset pagination pipeline and returns the projected page result.
    /// </summary>
    protected async ValueTask<SeekResult<TResult>> ExecuteAsync(CancellationToken cancellationToken)
    {
        _request ??= new SeekRequest { PageSize = _options.DefaultPageSize };
        return await SeekPagingAlgorithm.ExecuteAsync(_request, _options, _serializer, FetchAsync, CreateCursor, cancellationToken);
    }

    private Task<List<TResult>> FetchAsync(SeekData? seekData, SeekDirection direction, int limit, CancellationToken cancellationToken)
    {
        IQueryable<T> query = _query.ApplyOrdering(_sortColumns, direction);

        if (seekData is not null)
        {
            if (_filterStrategy is IPageSizeAware pageSizeFilter)
                pageSizeFilter.SetPageSize(limit);

            query = _filterStrategy.ApplyFilter(query, Expression.Parameter(typeof(T), "e"), _sortColumns, seekData);
        }

        IQueryable<TResult> transformed = _transformer(query.Take(limit));
        return MaterializeAsync(transformed, cancellationToken);
    }

    private string CreateCursor(TResult item, SeekDirection direction)
    {
        var stringValues = new Dictionary<string, string>(_sortColumns.Count);

        for (int i = 0; i < _sortColumns.Count; i++)
        {
            var value = _resultAccessors[i](item);
            var stringValue = _valueConverter.ToString(value, _sortColumns[i].KeyType);
            stringValues[_sortColumns[i].PropertyPath] = stringValue ?? string.Empty;
        }

        return _serializer.Serialize(new SeekData { Values = stringValues, Direction = direction });
    }
}
