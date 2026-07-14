namespace SeekKit.Core.Converters;

internal sealed class ULongConverter : TypeConverter<ulong>
{
    public override string ToString(ulong value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public override ulong FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
        if (!ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Cannot convert '{value}' to ulong");

        return result;
    }
}