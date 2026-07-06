namespace SeekKit.EntityFramework.Converters;

internal sealed class NullableTimeSpanConverter : TypeConverter<TimeSpan?>
{
    public override string? ToString(TimeSpan? value)
    {
        return value?.Ticks.ToString(CultureInfo.InvariantCulture) ?? null;
    }

    public override TimeSpan? FromString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ticks))
            throw new FormatException($"Cannot convert '{value}' to TimeSpan ticks");

        return TimeSpan.FromTicks(ticks);
    }
}