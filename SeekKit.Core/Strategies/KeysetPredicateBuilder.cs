namespace SeekKit.Core.Strategies;

/// <summary>
/// Builds the OR-expanded keyset predicate
/// <c>(a &gt; @a) OR (a == @a AND b &gt; @b) OR ...</c> as a LINQ expression.
/// Shared by <see cref="OrLogicSeekStrategy"/> (applied to an
/// <see cref="IQueryable{T}"/> via <c>Where</c>) and the MongoDB aggregation
/// builder (applied to an <c>IAggregateFluent&lt;T&gt;</c> via <c>Match</c>).
/// </summary>
public static class KeysetPredicateBuilder
{
    /// <summary>
    /// Builds the keyset predicate as a lambda. Returns <c>null</c> when there
    /// are no sort columns (i.e. no boundary to filter by).
    /// </summary>
    public static Expression<Func<T, bool>>? BuildOrPredicate<T>(
        IReadOnlyList<ISortColumn<T>> sortColumns,
        SeekData seekData,
        ISeekValueConverter valueConverter)
    {
        var parameter = Expression.Parameter(typeof(T), "e");
        var body = BuildOrPredicateBody(parameter, sortColumns, seekData, valueConverter);
        return body is null ? null : Expression.Lambda<Func<T, bool>>(body, parameter);
    }

    /// <summary>
    /// Builds the predicate body against an existing <paramref name="parameter"/>.
    /// Used by the <see cref="IQueryable{T}"/> path, which supplies its own parameter.
    /// </summary>
    internal static Expression? BuildOrPredicateBody<T>(
        ParameterExpression parameter,
        IReadOnlyList<ISortColumn<T>> sortColumns,
        SeekData seekData,
        ISeekValueConverter valueConverter)
    {
        Expression? finalPredicate = null;
        var valueHolders = sortColumns.ToSeekValueHolders(seekData.Values, valueConverter);
        var direction = seekData.Direction;

        for (int i = 0; i < sortColumns.Count; i++)
        {
            Expression? levelPredicate = null;

            // Equal conditions for previous fields
            for (int j = 0; j < i; j++)
            {
                var equalCondition = sortColumns[j].CreateCondition(
                    parameter, direction, isComparison: false, valueHolders);

                levelPredicate = levelPredicate == null
                    ? equalCondition
                    : Expression.AndAlso(levelPredicate, equalCondition);
            }

            // Comparison condition for the current field
            var compareCondition = sortColumns[i].CreateCondition(
                parameter, direction, isComparison: true, valueHolders);

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
