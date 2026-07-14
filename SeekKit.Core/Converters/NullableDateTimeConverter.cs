namespace SeekKit.Core.Converters;

internal sealed class NullableDateTimeConverter : TypeConverter<DateTime?>
{
    public override string? ToString(DateTime? value)
    {
        return value?.ToString("O", CultureInfo.InvariantCulture) ?? null;
    }

    public override DateTime? FromString(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? null
            : DateTime.ParseExact(
                value,
                "O",
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind);
    }
}