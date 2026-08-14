namespace SeekKit.MongoDB.Builders;

/// <summary>
/// MongoDB aggregation-pipeline implementation of the SeekKit builder. Paginates
/// an <see cref="IAggregateFluent{T}"/> — e.g. the result of
/// <c>collection.Aggregate().UnionWith(other)</c> or a pipeline with
/// <c>$lookup</c>/<c>$match</c> stages — by appending a keyset <c>$match</c>,
/// a <c>$sort</c>, and a <c>$limit</c>.
/// <para>
/// The keyset algorithm (token decode, look-ahead, token generation) is shared
/// with the LINQ builders via <see cref="SeekBuilderCore{T}"/>.
/// </para>
/// </summary>
internal sealed class SeekAggregateBuilder<T> : SeekBuilderCore<T>, ISeekMongoAggregateBuilder<T>
{
    private readonly IAggregateFluent<T> _aggregate;
    private readonly SeekKitMongoOptions _options;

    public SeekAggregateBuilder(
        IAggregateFluent<T> aggregate,
        ISeekSerializer serializer,
        ISeekValueConverter valueConverter,
        SeekKitMongoOptions options)
        : base(serializer, valueConverter, options)
    {
        _aggregate = aggregate ?? throw new ArgumentNullException(nameof(aggregate));
        _options   = options;
    }

    public ISeekMongoAggregateBuilder<T> WithRequest(SeekRequest request)
    {
        SetRequest(request);
        return this;
    }

    ISeekMongoBuilder<T> ISeekMongoBuilder<T>.WithRequest(SeekRequest request) => WithRequest(request);

    public ISeekMongoAggregateBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName = null)
    {
        AddSortColumn(keySelector, isDescending: false, resultPropertyName);
        return this;
    }

    ISeekMongoBuilder<T> ISeekMongoBuilder<T>.OrderBy<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName) => OrderBy(keySelector, resultPropertyName);

    public ISeekMongoAggregateBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName = null)
    {
        AddSortColumn(keySelector, isDescending: true, resultPropertyName);
        return this;
    }

    ISeekMongoBuilder<T> ISeekMongoBuilder<T>.OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector, string? resultPropertyName) => OrderByDescending(keySelector, resultPropertyName);

    public ISeekMongoBuilder<T, TResult> Select<TResult>(Func<IAggregateFluent<T>, IAggregateFluent<TResult>> transformer)
    {
        return new SeekAggregateProjectionBuilder<T, TResult>(
            _aggregate, SortColumns, transformer, Serializer, ValueConverter, Request, _options);
    }

    public ValueTask<SeekResult<T>> ToSeekResultAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(cancellationToken);

    protected override async Task<List<T>> FetchAsync(
        SeekData? seekData,
        SeekDirection direction,
        int limit,
        CancellationToken cancellationToken)
    {
        var pipeline = _aggregate;

        // Keyset boundary → $match. Placed before $sort so the planner can use an
        // index that covers both the filter and the sort.
        if (seekData is not null)
        {
            var predicate = KeysetPredicateBuilder.BuildOrPredicate(SortColumns, seekData, ValueConverter);
            if (predicate is not null)
                pipeline = pipeline.Match(predicate);
        }

        pipeline = AggregateOrderingHelper.ApplyOrdering(pipeline, SortColumns, direction);

        return await pipeline.Limit(limit).ToListAsync(cancellationToken);
    }
}
