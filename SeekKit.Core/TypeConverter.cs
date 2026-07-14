namespace SeekKit.Core;

internal interface ITypeConverter
{
    string? ToString(object? value);
    object? FromString(string value);
}

/// <summary>
/// Base class for converting values of type <typeparamref name="T"/> to and from their
/// string representation for cursor-token encoding.
/// <para>
/// Implement this class and register it via
/// <see cref="SeekKitConfiguration.AddConverter{T}(TypeConverter{T})"/>
/// to support column types not covered by the built-in converters.
/// </para>
/// </summary>
/// <typeparam name="T">The CLR type this converter handles.</typeparam>
/// <example>
/// <code>
/// public class StatusConverter : TypeConverter&lt;OrderStatus&gt;
/// {
///     public override string? ToString(OrderStatus value) =&gt; ((int)value).ToString();
///     public override OrderStatus FromString(string? value) =&gt; (OrderStatus)int.Parse(value!);
/// }
///
/// // Registration:
/// services.AddSeekKit(options => { ... }, cfg => cfg.AddConverter(new StatusConverter()));
/// </code>
/// </example>

public abstract class TypeConverter<T> : ITypeConverter
{
    /// <summary>
    /// Serializes <paramref name="value"/> to a stable, round-trippable string
    /// for inclusion in the cursor token.
    /// </summary>
    /// <param name="value">The column value to serialize.</param>
    public abstract string? ToString(T value);

    /// <summary>
    /// Parses a cursor-encoded string back to a <typeparamref name="T"/> value.
    /// </summary>
    /// <param name="value">
    /// The encoded string from the cursor token.
    /// May be <c>null</c> when the original column value was empty.
    /// </param>
    public abstract T FromString(string? value);

    string? ITypeConverter.ToString(object? value)
    {
        if (value == null) return null;
        return ToString((T)value);
    }

    object? ITypeConverter.FromString(string? value)
    {
        if (value == null) return default(T);
        return FromString(value);
    }
}
