namespace SeekKit.EntityFramework.Converters;

internal sealed class FloatConverter : TypeConverter<float>
{
    public override string ToString(float value)
    {
        // Use round-trip format to preserve precision
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    public override float FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Cannot convert '{value}' to float");

        return result;
    }
}