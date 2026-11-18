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

            // No branch at all means no row can lie beyond the boundary (it is the very
            // last row of the scan — every sort value NULL, with nulls sorted last).
            if (branches.Count == 0)
                return query.Where(_ => false);

            if (branches.Count == 1)
                return branches[0];

            var unionQuery = branches[0];
            for (int i = 1; i < branches.Count; i++)
            {
                unionQuery = unionQuery.Concat(branches[i]);
            }

            return unionQuery.ApplyOrdering(orderFields, seekData.Direction).Take(_pageSize);
        }
        catch (SeekTokenException)
        {
            // A bad token is the caller's problem, not a strategy failure — no fallback can fix it.
            throw;
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
        var direction = SeekData.Direction;

        // One branch per index range: ties on the preceding columns, strictly after the
        // boundary on the current one (a nullable column adds a separate IS NULL branch).
        foreach (var branch in KeysetPredicateBuilder.BuildBranchPredicates(parameter, orderFields, SeekData, _valueConverter))
        {
            // Build branch query
            var lambda = Expression.Lambda<Func<T, bool>>(branch.Predicate, parameter);

            // Inside a branch the columns it pins to one value are constant, so the branch
            // is ordered by the remaining ones only. This is not cosmetic: MySQL does not
            // see through `WHERE a IS NULL ORDER BY a, b` and sorts every matching row
            // (measured: 780 ms instead of 3 ms), while `ORDER BY b` reads the index in
            // order. The outer query still orders by all columns.
            var branchOrder = branch.PinnedColumns > 0 && branch.PinnedColumns < orderFields.Count
                ? orderFields.Skip(branch.PinnedColumns).ToArray()
                : orderFields;

            var branchQuery = baseQuery.Where(lambda).ApplyOrdering(branchOrder, direction).Take(_pageSize);
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
