namespace SeekKit.Core.Helpers;

/// <summary>
/// Compiles and caches delegates that read a named (optionally dot-separated) public
/// property path off an arbitrary type. Used by <c>SeekProjectionBuilderBase&lt;T, TResult&gt;</c>
/// to read cursor values from a projected result type, since after projection the
/// original entity instances are no longer available.
/// </summary>
internal static class ResultKeyAccessor
{
    private static readonly ConcurrentDictionary<(Type Type, string Path), Delegate> _cache = new();

    public static Func<TResult, object?> GetAccessor<TResult>(string propertyPath)
    {
        var key = (typeof(TResult), propertyPath);
        return (Func<TResult, object?>)_cache.GetOrAdd(key, static k => Build<TResult>(k.Path));
    }

    private static Func<TResult, object?> Build<TResult>(string propertyPath)
    {
        var param = Expression.Parameter(typeof(TResult), "x");
        Expression body = param;

        foreach (var segment in propertyPath.Split('.'))
        {
            var property = body.Type.GetProperty(segment, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new InvalidOperationException(
                    $"Cannot compute seek cursor: type '{typeof(TResult).Name}' has no public property '{segment}' (from sort column '{propertyPath}'). The projected result type must expose the same property names used in OrderBy/OrderByDescending so SeekKit can read cursor values after projection.");

            body = Expression.Property(body, property);
        }

        var converted = Expression.Convert(body, typeof(object));
        return Expression.Lambda<Func<TResult, object?>>(converted, param).Compile();
    }
}
