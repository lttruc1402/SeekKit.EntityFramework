namespace SeekKit.EntityFramework.Helpers;

internal static class TupleReflectionHelper
{
#if NET8_0_OR_GREATER
    private static FrozenDictionary<string, MethodInfo> _methods = FrozenDictionary<string, MethodInfo>.Empty;
#else
    // ConcurrentDictionary used as a lock-free cache on older runtimes.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, MethodInfo>
        _methods = new(StringComparer.Ordinal);
#endif

    public static Expression CreateValueTupleExpression(List<Expression> elements)
    {
        var types = elements.Select(e => e.Type).ToArray();
        var names = string.Join(", ", types.Select(e => e.FullName));

#if NET8_0_OR_GREATER
        var methods = _methods;
        if (!methods.TryGetValue(names, out var methodInfo))
        {
            methodInfo = typeof(ValueTuple)
                .GetMethods()
                .First(m => m.Name == "Create" && m.GetParameters().Length == elements.Count)
                .MakeGenericMethod(types);

            ImmutableInterlocked.Update(ref _methods, old =>
            {
                if (old.ContainsKey(names))
                    return old;

                return new Dictionary<string, MethodInfo>(old)
                {
                    { names, methodInfo }
                }.ToFrozenDictionary();
            });
        }
#else
        var methodInfo = _methods.GetOrAdd(names, _ =>
            typeof(ValueTuple)
                .GetMethods()
                .First(m => m.Name == "Create" && m.GetParameters().Length == elements.Count)
                .MakeGenericMethod(types));
#endif

        return Expression.Call(null, methodInfo, elements);
    }
}
