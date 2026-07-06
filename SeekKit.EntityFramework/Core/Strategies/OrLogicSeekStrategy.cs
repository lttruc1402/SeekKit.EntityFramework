namespace SeekKit.EntityFramework.Core.Strategies;

internal sealed class OrLogicSeekStrategy : ISeekFilterStrategy
{
    private readonly ISeekValueConverter _valueConverter;

    public OrLogicSeekStrategy(ISeekValueConverter valueConverter)
    {
        _valueConverter = valueConverter;
    }

    public IQueryable<T> ApplyFilter<T>(IQueryable<T> query, ParameterExpression parameter, List<ISortColumn<T>> orderFields, SeekData seekData)
    {
        var predicate = BuildOrPredicate(parameter, orderFields, seekData);

        if (predicate != null)
        {
            var lambda = Expression.Lambda<Func<T, bool>>(predicate, parameter);
            return query.Where(lambda);
        }

        return query;
    }
    private Expression? BuildOrPredicate<T>(
       ParameterExpression parameter,
       List<ISortColumn<T>> orderFields,
       SeekData SeekData)
    {
        Expression? finalPredicate = null;
        var SeekValueHolders = orderFields.ToSeekValueHolders(SeekData.Values, _valueConverter);
        var direction = SeekData.Direction;
        for (int i = 0; i < orderFields.Count; i++)
        {
            Expression? levelPredicate = null;

            // Equal conditions for previous fields
            for (int j = 0; j < i; j++)
            {
                var equalCondition = orderFields[j].CreateCondition(
                    parameter,
                    direction,
                    isComparison: false,
                    SeekValueHolders);

                levelPredicate = levelPredicate == null
                    ? equalCondition
                    : Expression.AndAlso(levelPredicate, equalCondition);
            }

            // Comparison condition for current field
            var compareCondition = orderFields[i].CreateCondition(
                parameter,
                direction,
                isComparison: true,
                SeekValueHolders);

            levelPredicate = levelPredicate == null
                ? compareCondition
                : Expression.AndAlso(levelPredicate, compareCondition);

            finalPredicate = finalPredicate == null
                ? levelPredicate
                : Expression.OrElse(finalPredicate, levelPredicate);
        }

        return finalPredicate;
    }
}
