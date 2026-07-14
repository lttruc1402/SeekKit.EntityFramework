namespace SeekKit.Core.Converters;

internal sealed class NullableFloatConverter : TypeConverter<float?>
{
    public override float? FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Cannot convert '{value}' to float");

        return result;
    }

    public override string? ToString(float? value)
    {
        return value?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
