namespace SeekKit.EntityFramework.Converters;

internal sealed class SByteConverter : TypeConverter<sbyte>
{
    public override string? ToString(sbyte value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public override sbyte FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
        if (!sbyte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Cannot convert '{value}' to sbyte");

        return result;
    }
}