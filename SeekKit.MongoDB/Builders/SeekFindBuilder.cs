namespace SeekKit.MongoDB.Builders;

/// <summary>
/// MongoDB <see cref="IFindFluent{T, T}"/> implementation of the SeekKit builder.
/// Paginates a find query — e.g. <c>collection.Find(bsonFilter)</c> with a
/// BSON <see cref="FilterDefinition{T}"/> (text search, geo, or any filter the
/// LINQ provider can't express) — by AND-ing the keyset predicate into the
/// query filter, then appending a sort and a limit.
/// <para>
/// The keyset algorithm (token decode, look-ahead, token generation) is shared
/// with the other builders via <see cref="SeekBuilderCore{T}"/>.
/// </para>
/// </summary>
internal sealed class SeekFindBuilder<T> : SeekBuilderCore<T>, ISeekMongoFindBuilder<T>
{
    private readonly IFindFluent<T, T> _find;
    private readonly SeekKitMongoOptions _options;
    private readonly FilterDefinition<T> _baseFilter;

    public SeekFindBuilder(
        IFindFluent<T, T> find,
        ISeekSerializer serializer,
        ISeekValueConverter valueConverter,
        SeekKitMongoOptions options)
        : base(serializer, valueConverter, options)
    {
        _find    = find ?? throw new ArgumentNullException(nameof(find));
        _options = options;
        _baseFilter = _find.Filter;
    }

    public ISeekMongoFindBuilder<T> WithRequest(SeekRequest request)
    {
        SetRequest(request);
        return this;
    }

    ISeekMongoBuilder<T> ISeekMongoBuilder<T>.WithRequest(SeekRequest request) => WithRequest(request);

    public ISeekMongoFindBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName = null)
    {
        AddSortColumn(keySelector, isDescending: false, resultPropertyName);
        return this;
    }

    ISeekMongoBuilder<T> ISeekMongoBuilder<T>.OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName) => OrderBy(keySelector, resultPropertyName);

    public ISeekMongoFindBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName = null)
    {
        AddSortColumn(keySelector, isDescending: true, resultPropertyName);
        return this;
    }

    ISeekMongoBuilder<T> ISeekMongoBuilder<T>.OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName) => OrderByDescending(keySelector, resultPropertyName);

    public ISeekMongoBuilder<T, TResult> Select<TResult>(Func<IFindFluent<T, T>, IFindFluent<T, TResult>> transformer)
    {
        return new SeekFindProjectionBuilder<T, TResult>(
            _find, SortColumns.ToArray(), transformer, Serializer, ValueConverter, Request, _options, IncludeTotalCount);
    }

    public ValueTask<SeekResult<T>> ToSeekResultAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(cancellationToken);

    public ISeekMongoFindBuilder<T> WithTotalCount(bool include = true)
    {
        SetIncludeTotalCount(include);
        return this;
    }

    ISeekMongoBuilder<T> ISeekMongoBuilder<T>.WithTotalCount(bool include) => WithTotalCount(include);

    public IAsyncEnumerable<SeekResult<T>> ToPagesAsync(CancellationToken cancellationToken = default)
        => ExecutePagesAsync(cancellationToken);

    public IAsyncEnumerable<T> ToItemsAsync(CancellationToken cancellationToken = default)
        => ExecuteItemsAsync(cancellationToken);

    protected override Task<long> CountAsync(CancellationToken cancellationToken)
        => MongoCount.CountAsync(_find, _baseFilter, cancellationToken);

    protected override async Task<List<T>> FetchAsync(
        SeekData? seekData,
        SeekDirection direction,
        int limit,
        CancellationToken cancellationToken)
    {
        // Keyset boundary → AND it into the existing find filter.
        // IFindFluent is mutable: always start again from the filter the caller supplied,
        // so walking several pages with one builder doesn't stack keyset predicates.
        _find.Filter = _baseFilter;

        if (seekData is not null)
        {
            var predicate = KeysetPredicateBuilder.BuildOrPredicate(SortColumns, seekData, ValueConverter);
            if (predicate is not null)
                _find.Filter = Builders<T>.Filter.And(_baseFilter, predicate);
        }

        var ordered = FindOrderingHelper.ApplyOrdering(_find, SortColumns, direction);

        return await ordered.Limit(limit).ToListAsync(cancellationToken);
    }
}
