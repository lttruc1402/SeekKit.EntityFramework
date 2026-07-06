namespace SeekKit.EntityFramework.Converters;

internal sealed class TimeSpanConverter : TypeConverter<TimeSpan>
{
    public override string? ToString(TimeSpan value)
    {
        // Use Ticks for precision and easy sorting
        // Ticks: 1 tick = 100 nanoseconds
        return value.Ticks.ToString(CultureInfo.InvariantCulture);
    }

    public override TimeSpan FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
            throw new FormatException($"Cannot convert '{value}' to TimeSpan ticks");

        return TimeSpan.FromTicks(ticks);
    }
}