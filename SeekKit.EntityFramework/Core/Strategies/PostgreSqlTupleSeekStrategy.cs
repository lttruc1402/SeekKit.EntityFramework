namespace SeekKit.EntityFramework.Core.Strategies;

internal sealed class PostgreSqlTupleSeekStrategy: ISeekFilterStrategy, IPageSizeAware
{
    private readonly ISeekValueConverter _valueConverter;
    private readonly FallbackStrategy _fallbackStrategy;
    private int _pageSize;

    public PostgreSqlTupleSeekStrategy(ISeekValueConverter valueConverter, FallbackStrategy fallbackStrategy)
    {
        _valueConverter = valueConverter;
        _fallbackStrategy = fallbackStrategy;
    }

    public IQueryable<T> ApplyFilter<T>(IQueryable<T> query, ParameterExpression parameter, List<ISortColumn<T>> orderFields, SeekData seekData)
    {
        if (orderFields.CanUseTuple())
        {
            var values = ApplyTupleComparison(query, parameter, orderFields, seekData);

            if(values != null)
                return values;
        }

        return ApplyFallback(query, parameter, orderFields, seekData);
    }

    public void SetPageSize(int pageSize)
    {
        _pageSize = pageSize;
    }

    private IQueryable<T>? ApplyTupleComparison<T>(
           IQueryable<T> query,
           ParameterExpression parameter,
           List<ISortColumn<T>> orderFields,
           SeekData SeekData)
    {


        var SeekValueHolders = orderFields.ToSeekValueHolders(SeekData.Values, _valueConverter);



        var leftExpressions = new List<Expression>(orderFields.Count);
        var rightExpressions = new List<Expression>(orderFields.Count);

        foreach (var field in orderFields)
        {
            var  (propertyExp, constantExp) = GetPropertyAndConstantShared(parameter, field, SeekValueHolders);
            leftExpressions.Add(propertyExp);
            rightExpressions.Add(constantExp);
        }

        var originDescending = orderFields[0].IsDescending;

        var isDescending = SeekData.Direction == SeekDirection.Previous
                    ? !originDescending
                    : originDescending;

        var methodName = isDescending ? "LessThan" : "GreaterThan";

        

        var leftTuple = TupleReflectionHelper.CreateValueTupleExpression(leftExpressions);
        var rightTuple = TupleReflectionHelper.CreateValueTupleExpression(rightExpressions);

        var leftItuple = Expression.Convert(leftTuple, typeof(System.Runtime.CompilerServices.ITuple));
        var rightItuple = Expression.Convert(rightTuple, typeof(System.Runtime.CompilerServices.ITuple));

        var npgsqlMethod = NpgsqlReflectionHelper.GetTupleComparisonMethod(methodName, leftTuple.Type, rightTuple.Type);

        if (npgsqlMethod != null)
        {
            // CASE: POSTGRESQL - Ph�t sinh SQL: (a, b) > (c, d)
            var efFunctions = Expression.Property(null, typeof(EF), nameof(EF.Functions));
            var call = Expression.Call(null, npgsqlMethod, efFunctions, leftItuple, rightItuple);
            var filter = Expression.Lambda<Func<T, bool>>(call, parameter);

            return query.Where(filter);
        }

        return null;
    }


    private IQueryable<T> ApplyFallback<T>(
           IQueryable<T> query,
           ParameterExpression parameter,
           List<ISortColumn<T>> orderFields,
           SeekData seekData)
    {
        ISeekFilterStrategy fallbackStrategy = _fallbackStrategy switch
        {
            FallbackStrategy.UnionAll =>
                new UnionAllSeekStrategy(_valueConverter, FallbackStrategy.OrLogic),

            FallbackStrategy.OrLogic =>
                new OrLogicSeekStrategy(_valueConverter),

            FallbackStrategy.None =>
                throw new InvalidOperationException("Cannot use tuple comparison (mixed directions or too many fields) and no fallback strategy configured"),

            _ => new OrLogicSeekStrategy(_valueConverter)
        };

        if (fallbackStrategy is IPageSizeAware pageSizeFilter)
            pageSizeFilter.SetPageSize(_pageSize);

        return fallbackStrategy.ApplyFilter(
            query, parameter, orderFields, seekData);
    }

    private static (Expression PropertyExp, Expression ConstantExp) GetPropertyAndConstantShared<T>(
        ParameterExpression parameter,
        ISortColumn<T> field,
        Dictionary<string, SeekValueHolder> sharedValueHolders)
    {
        // 1. Get property expression
        var selector = field.KeySelector;
        var visitor = new ParameterReplaceVisitor(selector.Parameters[0], parameter);
        var propertyExp = visitor.Visit(selector.Body);

        // 2. Get shared value holder
        if (!sharedValueHolders.TryGetValue(field.PropertyPath, out var valueHolder))
        {
            throw new InvalidOperationException($"Cursor value for '{field.PropertyPath}' not found");
        }

        // ? Use SAME holder instance across all branches
        var holderConstant = Expression.Constant(valueHolder);
        var valueProperty = Expression.Property(
            holderConstant,
            nameof(SeekValueHolder.Value));
        var constantExp = Expression.Convert(valueProperty, field.KeyType);

        return (propertyExp, constantExp);
    }
}
