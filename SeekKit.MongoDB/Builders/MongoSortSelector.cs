namespace SeekKit.MongoDB.Builders;

/// <summary>
/// Converts a sort column's typed key selector into the
/// <c>Expression&lt;Func&lt;T, object&gt;&gt;</c> shape the driver's
/// <c>SortBy</c>/<c>ThenBy</c> extensions expect, wrapping the body in a
/// convert-to-object so the driver still resolves the underlying field
/// (including <c>Id</c> → <c>_id</c>).
/// </summary>
internal static class MongoSortSelector
{
    internal static Expression<Func<T, object>> ToObjectSelector<T>(LambdaExpression keySelector)
    {
        if (keySelector.Body.Type == typeof(object))
            return (Expression<Func<T, object>>)keySelector;

        var body = Expression.Convert(keySelector.Body, typeof(object));
        return Expression.Lambda<Func<T, object>>(body, keySelector.Parameters);
    }
}
