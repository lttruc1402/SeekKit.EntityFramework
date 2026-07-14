namespace SeekKit.Core.Converters;

internal sealed class NullableSByteConverter : TypeConverter<sbyte?>
{
    public override string? ToString(sbyte? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? null;
    }

    public override sbyte? FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!sbyte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Cannot convert '{value}' to sbyte");

        return result;
    }
}