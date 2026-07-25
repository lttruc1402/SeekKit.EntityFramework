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

    private SeekRequest? _request;

    public SeekFindProjectionBuilder(
        IFindFluent<T, T> find,
        IReadOnlyList<ISortColumn<T>> sortColumns,
        Func<IFindFluent<T, T>, IFindFluent<T, TResult>> transformer,
        ISeekSerializer serializer,
        ISeekValueConverter valueConverter,
        SeekRequest? request,
        SeekOptionsBase options)
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

        _resultAccessors = new Func<TResult, object?>[_sortColumns.Count];
        for (int i = 0; i < _sortColumns.Count; i++)
        {
            _resultAccessors[i] = ResultKeyAccessor.GetAccessor<TResult>(_sortColumns[i].PropertyPath);
        }
    }

    public ValueTask<SeekResult<TResult>> ToSeekResultAsync(CancellationToken cancellationToken = default)
    {
        _request ??= new SeekRequest { PageSize = _options.DefaultPageSize };
        return SeekPagingAlgorithm.ExecuteAsync(_request, _options, _serializer, FetchAsync, CreateCursor, cancellationToken);
    }

    private async Task<List<TResult>> FetchAsync(SeekData? seekData, SeekDirection direction, int limit, CancellationToken cancellationToken)
    {
        if (seekData is not null)
        {
            var predicate = KeysetPredicateBuilder.BuildOrPredicate(_sortColumns, seekData, _valueConverter);
            if (predicate is not null)
                _find.Filter = Builders<T>.Filter.And(_find.Filter, predicate);
        }

        var ordered = FindOrderingHelper.ApplyOrdering(_find, _sortColumns, direction);
        var transformed = _transformer(ordered.Limit(limit));

        return await transformed.ToListAsync(cancellationToken);
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
