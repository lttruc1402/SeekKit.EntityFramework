namespace SeekKit.EntityFramework.Converters;

internal sealed class NullableULongConverter : TypeConverter<ulong?>
{
    public override string? ToString(ulong? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? null;
    }

    public override ulong? FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!ulong.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Cannot convert '{value}' to ulong");

        return result;
    }
}