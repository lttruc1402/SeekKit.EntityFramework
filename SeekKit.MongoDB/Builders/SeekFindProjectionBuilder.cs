namespace SeekKit.MongoDB.Builders;

/// <summary>
/// Projected (push-down) implementation of the SeekKit builder over an
/// <see cref="IFindFluent{T, T}"/>. AND-s the keyset predicate into the existing filter,
/// sorts, and limits exactly like <see cref="SeekFindBuilder{T}"/>, then hands the
/// already-limited find query to the transformer (via <c>IFindFluent{T,T}.Project</c>) so
/// the projection only runs against the limited set. Cursor values are read back from the
/// projected <typeparamref name="TResult"/> items via <see cref="ResultKeyAccessor"/>,
/// matched by sort-column property-path name.
/// </summary>
/// <typeparam name="T">The document type before projection.</typeparam>
/// <typeparam name="TResult">The projected result type returned to the caller.</typeparam>
internal sealed class SeekFindProjectionBuilder<T, TResult> : ISeekMongoBuilder<T, TResult>
{
    private readonly IFindFluent<T, T> _find;
    private readonly IReadOnlyList<ISortColumn<T>> _sortColumns;
    private readonly Func<IFindFluent<T, T>, IFindFluent<T, TResult>> _transformer;
    private readonly ISeekSerializer _serializer;
    private readonly ISeekValueConverter _valueConverter;
    private readonly SeekOptionsBase _options;
    private readonly Func<TResult, object?>[] _resultAccessors;

    private readonly bool _includeTotalCount;
    private readonly FilterDefinition<T> _baseFilter;
    private readonly SeekRequest? _request;

    public SeekFindProjectionBuilder(
        IFindFluent<T, T> find,
        IReadOnlyList<ISortColumn<T>> sortColumns,
        Func<IFindFluent<T, T>, IFindFluent<T, TResult>> transformer,
        ISeekSerializer serializer,
        ISeekValueConverter valueConverter,
        SeekRequest? request,
        SeekOptionsBase options,
        bool includeTotalCount)
    {
        if (sortColumns is null || sortColumns.Count == 0)
            throw new InvalidOperationException(
                "At least one order field is required. Use OrderBy() or OrderByDescending() before calling Select().");

        _find           = find ?? throw new ArgumentNullException(nameof(find));
        _sortColumns    = sortColumns;
        _transformer    = transformer ?? throw new ArgumentNullException(nameof(transformer));
        _serializer     = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _valueConverter = valueConverter ?? throw new ArgumentNullException(nameof(valueConverter));
        _options        = options ?? throw new ArgumentNullException(nameof(options));
        _request        = request;
        _includeTotalCount = includeTotalCount;
        _baseFilter     = find.Filter;

        _resultAccessors = new Func<TResult, object?>[_sortColumns.Count];
        for (int i = 0; i < _sortColumns.Count; i++)
        {
            var readPropertyPath = _sortColumns[i].PropertyPath;

            if (readPropertyPath == SeekIdentitySortColumn.PropertyPath)
                throw new InvalidOperationException(
                    "Cannot use Select() with a sort column added via an identity selector " +
                    $"(x => x) — there's no property name to match against the projected " +
                    $"{typeof(TResult).Name} shape. Pass the matching property name explicitly: " +
                    "OrderByDescending(x => x, resultPropertyName: \"Id\").");

            _resultAccessors[i] = ResultKeyAccessor.GetAccessor<TResult>(readPropertyPath);
        }
    }

    public ValueTask<SeekResult<TResult>> ToSeekResultAsync(CancellationToken cancellationToken = default)
        => CreatePager().PageAsync(_request, cancellationToken);

    public IAsyncEnumerable<SeekResult<TResult>> ToPagesAsync(CancellationToken cancellationToken = default)
        => CreatePager().PagesAsync(_request, cancellationToken);

    public IAsyncEnumerable<TResult> ToItemsAsync(CancellationToken cancellationToken = default)
        => CreatePager().ItemsAsync(_request, cancellationToken);

    private SeekPager<T, TResult> CreatePager()
        => new(
            _sortColumns,
            _serializer,
            _valueConverter,
            _options,
            FetchAsync,
            (item, index) => _resultAccessors[index](item),
            _includeTotalCount ? CountAsync : null);

    private Task<long> CountAsync(CancellationToken cancellationToken)
        => MongoCount.CountAsync(_find, _baseFilter, cancellationToken);

    private async Task<List<TResult>> FetchAsync(SeekData? seekData, SeekDirection direction, int limit, CancellationToken cancellationToken)
    {
        // IFindFluent is mutable: always start again from the filter the caller supplied,
        // so walking several pages with one builder doesn't stack keyset predicates.
        _find.Filter = _baseFilter;

        if (seekData is not null)
        {
            var predicate = KeysetPredicateBuilder.BuildOrPredicate(_sortColumns, seekData, _valueConverter);
            if (predicate is not null)
                _find.Filter = Builders<T>.Filter.And(_baseFilter, predicate);
        }

        var ordered = FindOrderingHelper.ApplyOrdering(_find, _sortColumns, direction);
        var transformed = _transformer(ordered.Limit(limit));

        return await transformed.ToListAsync(cancellationToken);
    }
}
