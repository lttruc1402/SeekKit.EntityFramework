namespace SeekKit.EntityFramework.Converters;

internal sealed class ShortConverter : TypeConverter<short>
{
    public override string? ToString(short value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public override short FromString(string? value)
    {
        if (!short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Cannot convert '{value}' to short");

        return result;
    }
}