namespace SeekKit.EntityFramework.Core.Strategies;

internal sealed class UnionAllSeekStrategy: ISeekFilterStrategy, IPageSizeAware
{
    private readonly ISeekValueConverter _valueConverter;
    private readonly FallbackStrategy _fallbackStrategy;
    
    private int _pageSize = 21;

    public UnionAllSeekStrategy(ISeekValueConverter valueConverter, FallbackStrategy fallbackStrategy)
    {
        _valueConverter = valueConverter;
        _fallbackStrategy = fallbackStrategy;
    }

    public IQueryable<T> ApplyFilter<T>(IQueryable<T> query, ParameterExpression parameter, IReadOnlyList<ISortColumn<T>> orderFields, SeekData seekData)
    {
        try
        {
            var branches = BuildBranchQueries(query, parameter, orderFields, seekData);

            if (branches.Count == 0)
                return query;

            if (branches.Count == 1)
                return branches[0];

            var unionQuery = branches[0];
            for (int i = 1; i < branches.Count; i++)
            {
                unionQuery = unionQuery.Concat(branches[i]);
            }

            return unionQuery.ApplyOrdering(orderFields, seekData.Direction).Take(_pageSize);
        }
        catch (Exception ex)
        {
            if (_fallbackStrategy == FallbackStrategy.None)
            {
                throw new InvalidOperationException("UNION ALL strategy failed and no fallback configured", ex);
            }

            return ApplyFallback(query, parameter, orderFields, seekData);
        }

        
    }

    public void SetPageSize(int pageSize)
    {
        _pageSize = pageSize;
    }



    private List<IQueryable<T>> BuildBranchQueries<T>(
            IQueryable<T> baseQuery,
            ParameterExpression parameter,
            IReadOnlyList<ISortColumn<T>> orderFields,
            SeekData SeekData)
    {
        var branches = new List<IQueryable<T>>(orderFields.Count);
        var SeekValueHolders = orderFields.ToSeekValueHolders(SeekData.Values, _valueConverter);
        var direction = SeekData.Direction;
        // Build each branch
        for (int i = 0; i < orderFields.Count; i++)
        {
            Expression? predicate = null;

            // Equal conditions for previous fields
            for (int j = 0; j < i; j++)
            {
                var equalCondition = orderFields[j].CreateCondition(
                    parameter,
                    direction,
                    isComparison: false,
                   SeekValueHolders);

                predicate = predicate == null
                    ? equalCondition
                    : Expression.AndAlso(predicate, equalCondition);
            }

            // Comparison condition for current field
            var compareCondition = orderFields[i].CreateCondition(
                parameter,
                direction,
                isComparison: true, SeekValueHolders);

            predicate = predicate == null
                ? compareCondition
                : Expression.AndAlso(predicate, compareCondition);

            // Build branch query
            var lambda = Expression.Lambda<Func<T, bool>>(predicate, parameter);
            var branchQuery = baseQuery.Where(lambda).ApplyOrdering(orderFields, direction).Take(_pageSize);
            branches.Add(branchQuery);
        }

        return branches;
    }
    

    private IQueryable<T> ApplyFallback<T>(
            IQueryable<T> query,
            ParameterExpression parameter,
            IReadOnlyList<ISortColumn<T>> orderFields,
             SeekData seekData)
    {
        if (_fallbackStrategy == FallbackStrategy.OrLogic)
        {
            var orStrategy = new OrLogicSeekStrategy(_valueConverter);
            return orStrategy.ApplyFilter(
                query, parameter, orderFields, seekData);
        }

        throw new InvalidOperationException("UNION ALL failed and no valid fallback configured");
    }
}
