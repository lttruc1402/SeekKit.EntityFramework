namespace SeekKit.Core.Converters;

internal sealed class DoubleConverter : TypeConverter<double>
{
    public override string? ToString(double value)
    {
        // Use round-trip format to preserve precision
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    public override double FromString(string? value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
            throw new FormatException($"Cannot convert '{value}' to double");

        return result;
    }
}