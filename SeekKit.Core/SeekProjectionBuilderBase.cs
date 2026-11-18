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

        _resultAccessors = ResolveResultAccessors(sortColumns, transformer);
    }

    /// <summary>
    /// Decides, per sort column, where on <typeparamref name="TResult"/> the cursor value is
    /// read from after projection:
    /// <list type="number">
    ///   <item><description>
    ///   a public property with the column's name (the sort property's own name, or the
    ///   explicit <c>resultPropertyName</c>) — the cheap, common case;
    ///   </description></item>
    ///   <item><description>
    ///   otherwise the property the value is traced to through the transformer's joins and
    ///   projections (<see cref="Helpers.SortKeyResultPathTracer{T, TResult}"/>) — this covers
    ///   identity selectors (<c>x =&gt; x</c>) and members renamed by the projection.
    ///   </description></item>
    /// </list>
    /// </summary>
    private static Func<TResult, object?>[] ResolveResultAccessors(
        IReadOnlyList<ISortColumn<T>> sortColumns,
        Func<IQueryable<T>, IQueryable<TResult>> transformer)
    {
        var accessors = new Func<TResult, object?>[sortColumns.Count];

        // Created on first use: probing the transformer is only needed for columns that
        // can't be read by name, and one probe serves all of them.
        Helpers.SortKeyResultPathTracer<T, TResult>? tracer = null;

        for (int i = 0; i < sortColumns.Count; i++)
        {
            var column = sortColumns[i];
            bool isIdentity = column.PropertyPath == SeekIdentitySortColumn.PropertyPath;

            if (!isIdentity && Helpers.ResultKeyAccessor.CanRead(typeof(TResult), column.PropertyPath))
            {
                accessors[i] = Helpers.ResultKeyAccessor.GetAccessor<TResult>(column.PropertyPath);
                continue;
            }

            tracer ??= new Helpers.SortKeyResultPathTracer<T, TResult>(transformer);
            var tracedPath = tracer.TryTrace(column.KeySelector);

            if (tracedPath is null)
            {
                if (SeekComputedSortColumn.IsComputed(column.PropertyPath))
                {
                    throw new InvalidOperationException(
                        $"Cannot use Select() with the computed sort expression '{column.KeySelector.Body}' " +
                        $"— the cursor is read back from the projected {typeof(TResult).Name}, and a " +
                        "computed value can't be traced to one of its properties. Project the same " +
                        "expression into a property of the result and name it when sorting, e.g. " +
                        "OrderBy(x => x.Name.ToLower(), resultPropertyName: \"NameLower\").");
                }

                if (!isIdentity)
                {
                    // Not readable by name and not traceable: surface the long-standing
                    // "no public property" error, which names the missing property.
                    accessors[i] = Helpers.ResultKeyAccessor.GetAccessor<TResult>(column.PropertyPath);
                    continue;
                }

                throw new InvalidOperationException(
                    "Cannot use Select() with a sort column added via an identity selector " +
                    $"(x => x) — couldn't trace which property on the projected " +
                    $"{typeof(TResult).Name} shape holds the equivalent value. The value is followed " +
                    "automatically through Join/LeftJoin/GroupJoin/SelectMany/Select chains as long as " +
                    "it is copied unchanged; it is lost when it is computed (e.g. Id = o.Id + 1), " +
                    "read from the optional side of an outer join, or built outside LINQ. In those " +
                    "cases pass the property name explicitly: " +
                    "OrderByDescending(x => x, resultPropertyName: \"Id\").");
            }

            accessors[i] = tracedPath.Length == 0
                ? static item => item                       // the projected item itself is the sort value
                : Helpers.ResultKeyAccessor.GetAccessor<TResult>(tracedPath);
        }

        return accessors;
    }

    /// <summary>
    /// Executes <paramref name="query"/> (already ordered, keyset-filtered, and
    /// transformed) using the provider's LINQ execution API.
    /// </summary>
    protected abstract Task<List<TResult>> MaterializeAsync(IQueryable<TResult> query, CancellationToken cancellationToken);

    /// <summary>
    /// Whether results carry <see cref="SeekResult{T}.TotalCount"/>. Derived builders set
    /// this from the builder <c>Select()</c> was called on.
    /// </summary>
    protected bool IncludeTotalCount { get; set; }

    /// <summary>
    /// Counts the source query — every row before pagination and before the transformer —
    /// using the provider's async API. The default runs the count synchronously, which is
    /// all a plain in-memory queryable can do; provider builders override it.
    /// </summary>
    protected virtual Task<long> LongCountAsync(IQueryable<T> query, CancellationToken cancellationToken)
        => Task.FromResult(query.LongCount());

    /// <summary>
    /// Runs the full keyset pagination pipeline and returns the projected page result.
    /// </summary>
    protected ValueTask<SeekResult<TResult>> ExecuteAsync(CancellationToken cancellationToken)
        => CreatePager().PageAsync(_request, cancellationToken);

    /// <summary>
    /// Returns the requested page and then every following one, fetching each lazily as
    /// the sequence is enumerated.
    /// </summary>
    protected IAsyncEnumerable<SeekResult<TResult>> ExecutePagesAsync(CancellationToken cancellationToken)
        => CreatePager().PagesAsync(_request, cancellationToken);

    /// <summary>
    /// Like <see cref="ExecutePagesAsync"/>, flattened to the individual items.
    /// </summary>
    protected IAsyncEnumerable<TResult> ExecuteItemsAsync(CancellationToken cancellationToken)
        => CreatePager().ItemsAsync(_request, cancellationToken);

    private SeekPager<T, TResult> CreatePager()
        => new(
            _sortColumns,
            _serializer,
            _valueConverter,
            _options,
            FetchAsync,
            (item, index) => _resultAccessors[index](item),
            IncludeTotalCount ? ct => LongCountAsync(_query, ct) : null);

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
}
