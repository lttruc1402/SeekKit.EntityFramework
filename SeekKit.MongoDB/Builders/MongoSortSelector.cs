namespace SeekKit.MongoDB.Builders;

/// <summary>
/// Adapts a typed sort key selector (<c>Expression&lt;Func&lt;T, TKey&gt;&gt;</c>) to the
/// <c>Expression&lt;Func&lt;T, object&gt;&gt;</c> shape the MongoDB fluent sort APIs
/// (<c>SortBy</c> / <c>ThenBy</c>) take.
/// </summary>
internal static class MongoSortSelector
{
    internal static Expression<Func<T, object>> ToObjectSelector<T>(LambdaExpression keySelector)
    {
        // $sort / find sort take field paths only. Fail here with something actionable
        // rather than let the driver reject the expression deep inside its translator.
        if (!SortKeyPathTracing.TryGetMemberChain(keySelector.Body, out var parameter, out _)
            || parameter != keySelector.Parameters[0])
        {
            throw new NotSupportedException(
                $"The sort expression '{keySelector.Body}' is computed, and the MongoDB aggregate/find " +
                "builders can only sort by a field. Either add the value as a field first " +
                "($addFields / $set) and sort by that, or use the IQueryable builder, whose LINQ " +
                "provider can translate computed sort keys.");
        }

        if (keySelector.Body.Type == typeof(object))
            return (Expression<Func<T, object>>)keySelector;

        var body = Expression.Convert(keySelector.Body, typeof(object));
        return Expression.Lambda<Func<T, object>>(body, keySelector.Parameters);
    }
}
