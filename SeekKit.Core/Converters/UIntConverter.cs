namespace SeekKit.Core.Converters;

internal sealed class UIntConverter : TypeConverter<uint>
{
    public override string ToString(uint value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public override uint FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
        if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Cannot convert '{value}' to uint");

        return result;
    }
}