namespace SeekKit.EntityFramework.Helpers;

internal static class NpgsqlReflectionHelper
{
#if NET8_0_OR_GREATER
    private static FrozenDictionary<string, MethodInfo?> _methods = FrozenDictionary<string, MethodInfo?>.Empty;
#else
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, MethodInfo?>
        _methods = new(StringComparer.Ordinal);
#endif

    private static Assembly? _assembly;

#if NET9_0_OR_GREATER
    private static readonly Lock _lock = new();
#else
    private static readonly object _lock = new();
#endif

    public static MethodInfo? GetTupleComparisonMethod(string methodName, Type leftType, Type rightType)
    {
        var cacheKey = $"{methodName}_{leftType.FullName}_{rightType.FullName}";

#if NET8_0_OR_GREATER
        var methods = _methods;
        if (methods.TryGetValue(cacheKey, out var cachedMethod))
            return cachedMethod;
#else
        if (_methods.TryGetValue(cacheKey, out var cachedMethod))
            return cachedMethod;
#endif

        if (_assembly is null)
        {
            lock (_lock)
            {
                if (_assembly is null)
                {
                    var npgsqlAssembly = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name == "Npgsql.EntityFrameworkCore.PostgreSQL");
                    _assembly = npgsqlAssembly;
                }
            }
        }

        MethodInfo? methodInfo = null;

#if NET8_0_OR_GREATER
        ImmutableInterlocked.Update(ref _methods, old =>
        {
            if (old.TryGetValue(cacheKey, out methodInfo))
                return old;

            if (_assembly is not null)
            {
                var extensionType = _assembly.GetType("Microsoft.EntityFrameworkCore.NpgsqlDbFunctionsExtensions");
                if (extensionType is not null)
                {
                    methodInfo = extensionType.GetMethods().FirstOrDefault(m =>
                    {
                        if (m.Name != methodName) return false;
                        var parameters = m.GetParameters();
                        return parameters.Length == 3 && parameters[1].ParameterType.IsAssignableFrom(leftType);
                    });
                }
            }

            return new Dictionary<string, MethodInfo?>(old)
            {
                { cacheKey, methodInfo }
            }.ToFrozenDictionary();
        });
#else
        _methods.GetOrAdd(cacheKey, _ =>
        {
            if (_assembly is null) return null;

            var extensionType = _assembly.GetType("Microsoft.EntityFrameworkCore.NpgsqlDbFunctionsExtensions");
            if (extensionType is null) return null;

            return extensionType.GetMethods().FirstOrDefault(m =>
            {
                if (m.Name != methodName) return false;
                var parameters = m.GetParameters();
                return parameters.Length == 3 && parameters[1].ParameterType.IsAssignableFrom(leftType);
            });
        });

        _methods.TryGetValue(cacheKey, out methodInfo);
#endif

        return methodInfo;
    }
}
