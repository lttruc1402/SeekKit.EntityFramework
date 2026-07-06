
namespace SeekKit.EntityFramework.Core;

/// <summary>
/// Converts keyset column values to and from their stable string representation
/// for cursor-token encoding. The default implementation delegates to registered
/// <see cref="TypeConverter{T}"/> instances.
/// </summary>
public interface ISeekValueConverter
{
    /// <summary>
    /// Converts <paramref name="value"/> to its stable string form for cursor encoding.
    /// Returns <c>null</c> when <paramref name="value"/> is <c>null</c>.
    /// </summary>
    /// <param name="value">The column value to convert.</param>
    /// <param name="type">The CLR type of the column.</param>
    string? ToString(object? value, Type type);

    /// <summary>
    /// Parses a cursor-encoded string back to the original CLR value.
    /// Returns <c>null</c> when <paramref name="value"/> is empty.
    /// </summary>
    /// <param name="value">The encoded string extracted from the cursor token.</param>
    /// <param name="type">The target CLR type.</param>
    object? FromString(string value, Type type);
}

internal sealed class SeekValueConverter : ISeekValueConverter
{
    private readonly ITypeConverterRegistry _registry;
    private static readonly Type _typeString = typeof(string);

    public SeekValueConverter(ITypeConverterRegistry registry)
    {
        _registry = registry;
    }

    public object? FromString(string value, Type type)
    {
        if (_typeString == type)
            return value;
        if (string.IsNullOrEmpty(value))
            return null;

        var converter = _registry.GetConverter(type);
        if (converter == null)
            throw new InvalidOperationException($"No converter registered for type {type.Name}");

        return converter.FromString(value);
    }

    public string? ToString(object? value, Type type)
    {
        if (_typeString == type)
        {
            if(value == null) return null;
            return (string)value;
        }

        if (value == null)
            return null;

        var converter = _registry.GetConverter(type);
        if (converter == null)
            throw new InvalidOperationException($"No converter registered for type {type.Name}");

        return converter.ToString(value);

    }
}