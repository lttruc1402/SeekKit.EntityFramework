namespace SeekKit.Core.Converters;

internal sealed class NullableDecimalConverter : TypeConverter<decimal?>
{
    public override string? ToString(decimal? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? null;
    }

    public override decimal? FromString(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? null
            : decimal.Parse(value, CultureInfo.InvariantCulture);
    }
}