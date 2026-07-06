namespace SeekKit.EntityFramework.Core;

/// <summary>
/// Registry for <see cref="TypeConverter{T}"/> instances used to serialize keyset
/// column values into cursor tokens. Built-in converters cover all .NET primitive
/// types, <see cref="Guid"/>, <see cref="DateTime"/>, <see cref="DateOnly"/>,
/// <see cref="TimeOnly"/>, <see cref="TimeSpan"/>, and their nullable variants.
/// Register custom converters via <see cref="SeekKitConfiguration.AddConverter{T}"/>.
/// </summary>
public interface ITypeConverterRegistry
{
    /// <summary>
    /// Registers a converter for type <typeparamref name="T"/>.
    /// Replaces any previously registered converter for the same type.
    /// </summary>
    /// <typeparam name="T">The CLR type the converter handles.</typeparam>
    /// <param name="converter">The converter instance to register.</param>
    void Register<T>(TypeConverter<T> converter);

    /// <summary>
    /// Returns the converter for type <typeparamref name="T"/>,
    /// or <c>null</c> if none is registered.
    /// </summary>
    TypeConverter<T>? GetConverter<T>();

    /// <summary>
    /// Returns the converter for <paramref name="type"/> as a non-generic
    /// <see cref="ITypeConverter"/>, or <c>null</c> if none is registered.
    /// </summary>
    internal ITypeConverter? GetConverter(Type type);

    /// <summary>
    /// Returns <c>true</c> if a converter is registered for <paramref name="type"/>.
    /// </summary>
    bool HasConverter(Type type);
}


internal sealed class TypeConverterRegistry : ITypeConverterRegistry
{
#if NET8_0_OR_GREATER
    private FrozenDictionary<Type, ITypeConverter> _converters = FrozenDictionary<Type, ITypeConverter>.Empty;
#else
    private readonly Dictionary<Type, ITypeConverter>
        _converters = [];
#endif

    public void Register<T>(TypeConverter<T> converter)
    {
        var type = typeof(T);
#if NET8_0_OR_GREATER
        ImmutableInterlocked.Update(ref _converters, old =>
        {
            return new Dictionary<Type, ITypeConverter>(old) { [type] = converter }
                .ToFrozenDictionary();
        });
#else
        _converters[type] = converter;
#endif
    }

    public TypeConverter<T>? GetConverter<T>()
    {
        return _converters.TryGetValue(typeof(T), out var converter)
            ? (TypeConverter<T>)converter
            : null;
    }

    public ITypeConverter? GetConverter(Type type)
    {
        return _converters.TryGetValue(type, out var converter)
            ? converter
            : null;
    }

    public bool HasConverter(Type type) => _converters.ContainsKey(type);
}
