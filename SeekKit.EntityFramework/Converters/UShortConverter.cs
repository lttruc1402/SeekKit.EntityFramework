namespace SeekKit.EntityFramework.Converters;

internal sealed class UShortConverter : TypeConverter<ushort>
{
    public override string? ToString(ushort value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public override ushort FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
        if (!ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Cannot convert '{value}' to ushort");

        return result;
    }
}