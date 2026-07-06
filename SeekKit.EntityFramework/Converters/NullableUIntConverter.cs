namespace SeekKit.EntityFramework.Converters;

internal sealed class NullableUIntConverter : TypeConverter<uint?>
{
    public override string? ToString(uint? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? null;
    }

    public override uint? FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Cannot convert '{value}' to uint");

        return result;
    }
}