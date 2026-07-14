namespace SeekKit.Core;

/// <summary>
/// Registry for <see cref="TypeConverter{T}"/> instances used to serialize keyset
/// column values into cursor tokens. Built-in converters cover all .NET primitive
/// types, <see cref="Guid"/>, <see cref="DateTime"/>, <c>DateOnly</c>,
/// <c>TimeOnly</c>, <see cref="TimeSpan"/>, and their nullable variants.
/// Register custom converters via
/// <see cref="SeekKitConfiguration.AddConverter{T}(TypeConverter{T})"/>.
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

    /// <summary>
    /// Creates a registry pre-populated with converters for all primitives,
    /// <see cref="Guid"/>, date/time types, and their nullable variants.
    /// </summary>
    internal static TypeConverterRegistry CreateDefault()
    {
        var registry = new TypeConverterRegistry();

        registry.Register(new ByteConverter());
        registry.Register(new NullableByteConverter());
        registry.Register(new SByteConverter());
        registry.Register(new NullableSByteConverter());

        registry.Register(new ShortConverter());
        registry.Register(new NullableShortConverter());
        registry.Register(new UShortConverter());
        registry.Register(new NullableUShortConverter());

        registry.Register(new IntConverter());
        registry.Register(new NullableIntConverter());
        registry.Register(new UIntConverter());
        registry.Register(new NullableUIntConverter());

        registry.Register(new LongConverter());
        registry.Register(new NullableLongConverter());
        registry.Register(new ULongConverter());
        registry.Register(new NullableULongConverter());

        registry.Register(new FloatConverter());
        registry.Register(new NullableFloatConverter());
        registry.Register(new DoubleConverter());
        registry.Register(new NullableDoubleConverter());
        registry.Register(new DecimalConverter());
        registry.Register(new NullableDecimalConverter());

        registry.Register(new BoolConverter());
        registry.Register(new NullableBoolConverter());
        registry.Register(new CharConverter());
        registry.Register(new NullableCharConverter());
        registry.Register(new StringConverter());

        registry.Register(new DateTimeConverter());
        registry.Register(new NullableDateTimeConverter());
        registry.Register(new DateTimeOffsetConverter());
        registry.Register(new NullableDateTimeOffsetConverter());

#if NET6_0_OR_GREATER
        registry.Register(new DateOnlyConverter());
        registry.Register(new NullableDateOnlyConverter());
        registry.Register(new TimeOnlyConverter());
        registry.Register(new NullableTimeOnlyConverter());
#endif

        registry.Register(new TimeSpanConverter());
        registry.Register(new NullableTimeSpanConverter());

        registry.Register(new GuidConverter());
        registry.Register(new NullableGuidConverter());

        return registry;
    }
}
