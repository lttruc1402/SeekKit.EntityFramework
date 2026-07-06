namespace SeekKit.EntityFramework.Converters;

internal sealed class DecimalConverter : TypeConverter<decimal>
{
    public override string? ToString(decimal value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public override decimal FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}