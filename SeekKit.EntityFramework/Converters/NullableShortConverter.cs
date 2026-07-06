namespace SeekKit.EntityFramework.Converters;

internal sealed class NullableShortConverter : TypeConverter<short?>
{
    public override string? ToString(short? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? null;
    }

    public override short? FromString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        if (!short.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Cannot convert '{value}' to short");

        return result;
    }
}