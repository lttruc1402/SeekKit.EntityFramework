#if NET6_0_OR_GREATER
namespace SeekKit.Core.Converters;

internal sealed class DateOnlyConverter : TypeConverter<DateOnly>
{
    internal const string Format = "yyyy-MM-dd";

    public override string ToString(DateOnly value)
    {
        return value.ToString(Format, CultureInfo.InvariantCulture);
    }

    public override DateOnly FromString(string? value)
    {
#if NET8_0_OR_GREATER
        ArgumentException.ThrowIfNullOrWhiteSpace(value, nameof(value));
#else
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
#endif
        if (!DateOnly.TryParseExact(
            value,
            Format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var result))
        {
            throw new FormatException(
                $"Cannot convert '{value}' to DateOnly. Expected format: {Format}");
        }

        return result;
    }
}
#endif
