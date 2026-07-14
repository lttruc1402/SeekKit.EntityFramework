#if NET6_0_OR_GREATER
namespace SeekKit.Core.Converters;

internal sealed class NullableTimeOnlyConverter : TypeConverter<TimeOnly?>
{
    public override string? ToString(TimeOnly? value)
    {
        return value?.ToString(TimeOnlyConverter.Format, CultureInfo.InvariantCulture) ?? null;
    }

    public override TimeOnly? FromString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        if (!TimeOnly.TryParseExact(
            value,
            TimeOnlyConverter.Format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var result))
        {
            throw new FormatException(
                $"Cannot convert '{value}' to TimeOnly. Expected format: {TimeOnlyConverter.Format}");
        }

        return result;
    }
}
#endif
