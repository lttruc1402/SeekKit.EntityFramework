namespace SeekKit.EntityFramework.Converters;

internal sealed class ByteConverter : TypeConverter<byte>
{
    public override string? ToString(byte value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public override byte FromString(string? value)
    {
        if (!byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Cannot convert '{value}' to byte");

        return result;
    }
}