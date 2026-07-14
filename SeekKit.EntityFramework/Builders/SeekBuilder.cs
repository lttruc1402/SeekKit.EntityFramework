namespace SeekKit.EntityFramework.Builders;

/// <summary>
/// EF Core implementation of the SeekKit builder. The keyset algorithm lives in
/// <see cref="SeekBuilderBase{T}"/> (SeekKit.Core); this class contributes the
/// EF-specific strategy resolution and async materialization.
/// </summary>
internal sealed class SeekBuilder<T> : SeekBuilderBase<T>, ISeekBuilder<T>
{
    private readonly SeekKitOptions _options;

    public SeekBuilder(IQueryable<T> query, ISeekSerializer seekSerializer, ISeekValueConverter valueConverter, SeekKitOptions options)
        : base(query, seekSerializer, valueConverter, options)
    {
        _options = options;
    }

    public ISeekBuilder<T> WithRequest(SeekRequest request)
    {
        SetRequest(request);
        return this;
    }

    public ValueTask<SeekResult<T>> ToSeekResultAsync(CancellationToken cancellationToken = default)
        => ExecuteAsync(cancellationToken);

    public ISeekBuilder<T> OrderBy<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        AddSortColumn(keySelector, isDescending: false);
        return this;
    }

    public ISeekBuilder<T> OrderByDescending<TKey>(Expression<Func<T, TKey>> keySelector)
    {
        AddSortColumn(keySelector, isDescending: true);
        return this;
    }

    public ISeekBuilder<T> WithStrategy(ISeekFilterStrategy strategy)
    {
        SetStrategy(strategy);
        return this;
    }

    public ISeekBuilder<T> WithTupleComparison()
    {
        if (_options.Strategy.DatabaseType == DatabaseType.PostgreSql)
            SetStrategy(new PostgreSqlTupleSeekStrategy(ValueConverter, FallbackStrategy.UnionAll));

        return this;
    }

    public ISeekBuilder<T> WithOrPredicate()
    {
        SetStrategy(new OrLogicSeekStrategy(ValueConverter));
        return this;
    }

    public ISeekBuilder<T> WithUnionAll()
    {
        SetStrategy(new UnionAllSeekStrategy(ValueConverter, FallbackStrategy.OrLogic));
        return this;
    }

    protected override ISeekFilterStrategy ResolveDefaultStrategy()
        => _options.Strategy.GetFilterStrategy(ValueConverter);

    protected override Task<List<T>> MaterializeAsync(IQueryable<T> query, CancellationToken cancellationToken)
        => query.ToListAsync(cancellationToken);
}
