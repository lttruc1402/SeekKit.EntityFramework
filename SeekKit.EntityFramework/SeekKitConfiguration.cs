namespace SeekKit.EntityFramework;

/// <summary>
/// Provides an API to extend SeekKit with custom type converters.
/// Passed as the second argument to <c>AddSeekKit(..., configure)</c>.
/// </summary>
public sealed class SeekKitConfiguration
{
    private readonly ITypeConverterRegistry _registry;
    private readonly IServiceCollection _services;

    public SeekKitConfiguration(ITypeConverterRegistry registry, IServiceCollection services)
    {
        _registry = registry;
        _services = services;
    }

    /// <summary>
    /// Registers a custom <see cref="TypeConverter{T}"/> for a type not supported out of the box.
    /// Built-in converters cover all .NET primitive types, <see cref="Guid"/>, <see cref="DateTime"/>,
    /// <see cref="DateOnly"/>, <see cref="TimeOnly"/>, <see cref="TimeSpan"/>, and their nullable variants.
    /// </summary>
    /// <typeparam name="T">The CLR type the converter handles.</typeparam>
    /// <param name="converter">The converter instance to register.</param>
    public SeekKitConfiguration AddConverter<T>(TypeConverter<T> converter)
    {
        _registry.Register(converter);
        return this;
    }


    public SeekKitConfiguration AddConverter<T>(Func<T, string?> toString, Func<string?, T> fromString)
    {
        return AddConverter(new DelegateTypeConverter<T>(toString, fromString));
    }


    public SeekKitConfiguration UseSeekSerializer<T>()
        where T : class, ISeekSerializer
    {
        _services.RemoveAll<ISeekSerializer>();
        _services.AddSingleton<ISeekSerializer, T>();
        return this;
    }

    /// <summary>
    /// Signs pagination tokens with HMAC-SHA256 so clients cannot tamper with or
    /// forge them. Tokens produced without the key (or with a different key) are
    /// rejected and treated as "first page".
    /// </summary>
    /// <param name="key">
    /// Secret key, at least 16 bytes — use a long random value (e.g. 32 bytes from
    /// a secure generator) stored in configuration, not in source code.
    /// </param>
    public SeekKitConfiguration UseHmacSigning(byte[] key)
    {
        _services.RemoveAll<ISeekSerializer>();
        _services.AddSingleton<ISeekSerializer>(new HmacSeekSerializer(key));
        return this;
    }

    /// <inheritdoc cref="UseHmacSigning(byte[])"/>
    public SeekKitConfiguration UseHmacSigning(string key)
    {
        if (key is null) throw new ArgumentNullException(nameof(key));
        return UseHmacSigning(System.Text.Encoding.UTF8.GetBytes(key));
    }
}
