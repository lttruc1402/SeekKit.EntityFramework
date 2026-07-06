namespace SeekKit.EntityFramework.Converters;

internal sealed class NullableDoubleConverter : TypeConverter<double?>
{
    public override string? ToString(double? value)
    {
        return value?.ToString("R", CultureInfo.InvariantCulture) ?? null;
    }

    public override double? FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Cannot convert '{value}' to double");

        return result;
    }
}