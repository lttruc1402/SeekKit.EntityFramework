namespace SeekKit.EntityFramework.Core.Strategies;

internal sealed class PostgreSqlAutoStrategy: ISeekFilterStrategy, IPageSizeAware
{
    private readonly ISeekValueConverter _valueConverter;
    private readonly FallbackStrategy _fallbackStrategy;
    private  int _pageSize;

    public PostgreSqlAutoStrategy(ISeekValueConverter valueConverter, FallbackStrategy fallbackStrategy)
    {
        _valueConverter = valueConverter;
        _fallbackStrategy = fallbackStrategy;
    }

    public IQueryable<T> ApplyFilter<T>(IQueryable<T> query, ParameterExpression parameter, List<ISortColumn<T>> orderFields, SeekData seekData)
    {
        if (orderFields.CanUseTuple())
        {
            var tupleStrategy = new PostgreSqlTupleSeekStrategy(_valueConverter, _fallbackStrategy);
            tupleStrategy.SetPageSize(_pageSize);

            return tupleStrategy.ApplyFilter(
                query, parameter, orderFields, seekData);
        }

        ISeekFilterStrategy fallbackStrategy = _fallbackStrategy switch
        {
            FallbackStrategy.UnionAll =>
                new UnionAllSeekStrategy(_valueConverter, FallbackStrategy.OrLogic),

            FallbackStrategy.OrLogic =>
                new OrLogicSeekStrategy(_valueConverter),

            _ => new UnionAllSeekStrategy(_valueConverter, FallbackStrategy.OrLogic)
        };

        if(fallbackStrategy is IPageSizeAware pageSizeFilter)
            pageSizeFilter.SetPageSize(_pageSize);

        return fallbackStrategy.ApplyFilter(
            query, parameter, orderFields, seekData);
    }

    public void SetPageSize(int pageSize)
    {
        _pageSize = pageSize;
    }
}
