
namespace SeekKit.EntityFramework.Converters;

internal sealed class DateTimeOffsetConverter : TypeConverter<DateTimeOffset>
{
    public override string ToString(DateTimeOffset value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    public override DateTimeOffset FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
        return DateTimeOffset.ParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }
}