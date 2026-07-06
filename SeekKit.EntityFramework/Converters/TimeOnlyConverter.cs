#if NET6_0_OR_GREATER
namespace SeekKit.EntityFramework.Converters;

internal sealed class TimeOnlyConverter : TypeConverter<TimeOnly>
{
    public const string Format = "HH:mm:ss.fffffff";

    public override string ToString(TimeOnly value)
    {
        return value.ToString(Format, CultureInfo.InvariantCulture);
    }

    public override TimeOnly FromString(string? value)
    {
#if NET8_0_OR_GREATER
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
#else
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
#endif
        if (!TimeOnly.TryParseExact(
            value,
            Format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var result))
        {
            throw new FormatException(
                $"Cannot convert '{value}' to TimeOnly. Expected format: {Format}");
        }

        return result;
    }
}
#endif
