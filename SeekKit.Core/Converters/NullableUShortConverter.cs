namespace SeekKit.Core.Converters;

internal sealed class NullableUShortConverter : TypeConverter<ushort?>
{
    public override string? ToString(ushort? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? null;
    }

    public override ushort? FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Cannot convert '{value}' to ushort");

        return result;
    }
}