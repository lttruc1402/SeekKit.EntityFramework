namespace SeekKit.EntityFramework.Converters;

internal sealed class DateTimeConverter : TypeConverter<DateTime>
{
    public override string? ToString(DateTime value)
    {
        // ISO 8601 round-trip format
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    public override DateTime FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
        return DateTime.ParseExact(
            value,
            "O",
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
    }
}