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

    public SeekFindBuilder(
        IFindFluent<T, T> find,
        ISeekSerializer serializer,
        ISeekValueConverter valueConverter,
        SeekKitMongoOptions options)
        : base(serializer, valueConverter, options)
    {
        _find    = find ?? throw new ArgumentNullException(nameof(find));
        _options = options;
    }

    public ISeekMongoFindBuilder<T> WithRequest(SeekRequest request)
    {
        SetRequest(request);
        return this;
    }

    ISeekMongoBuilder<T> ISeekMongoBuilder<T>.WithRequest(SeekRequest request) => WithRequest(request);

    public ISeekMongoFindBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        AddSortColumn(keySelector, isDescending: false);
        return this;
    }

    ISeekMongoBuilder<T> ISeekMongoBuilder<T>.OrderBy<TKey>(Expression<Func<T, TKey>> keySelector) => OrderBy(keySelector);

    public ISeekMongoFindBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        AddSortColumn(keySelector, isDescending: true);
        return this;
    }

    ISeekMongoBuilder<T> ISeekMongoBuilder<T>.OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector) => OrderByDescending(keySelector);

    public ISeekMongoBuilder<T, TResult> Select<TResult>(Func<IFindFluent<T, T>, IFindFluent<T, TResult>> transformer)
    {
        return new SeekFindProjectionBuilder<T, TResult>(
            _find, SortColumns, transformer, Serializer, ValueConverter, Request, _options);
    }

    public ValueTask<SeekResult<T>> ToSeekResultAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(cancellationToken);

    protected override async Task<List<T>> FetchAsync(
        SeekData? seekData,
        SeekDirection direction,
        int limit,
        CancellationToken cancellationToken)
    {
        // Keyset boundary → AND it into the existing find filter.
        if (seekData is not null)
        {
            var predicate = KeysetPredicateBuilder.BuildOrPredicate(SortColumns, seekData, ValueConverter);
            if (predicate is not null)
                _find.Filter = Builders<T>.Filter.And(_find.Filter, predicate);
        }

        var ordered = FindOrderingHelper.ApplyOrdering(_find, SortColumns, direction);

        return await ordered.Limit(limit).ToListAsync(cancellationToken);
    }
}
