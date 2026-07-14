namespace SeekKit.Core.Converters;

internal sealed class NullableByteConverter : TypeConverter<byte?>
{
    public override string? ToString(byte? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? null;
    }

    public override byte? FromString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        if (!byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Cannot convert '{value}' to byte");

        return result;
    }
}