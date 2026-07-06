#if NET6_0_OR_GREATER
namespace SeekKit.EntityFramework.Converters;

internal sealed class NullableDateOnlyConverter : TypeConverter<DateOnly?>
{
    public override string? ToString(DateOnly? value)
    {
        return value?.ToString(DateOnlyConverter.Format, CultureInfo.InvariantCulture) ?? null;
    }

    public override DateOnly? FromString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        if (!DateOnly.TryParseExact(
            value,
            DateOnlyConverter.Format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var result))
        {
            throw new FormatException(
                $"Cannot convert '{value}' to DateOnly. Expected format: {DateOnlyConverter.Format}");
        }

        return result;
    }
}
#endif
