namespace SeekKit.Core.Converters;

internal sealed class NullableDateTimeOffsetConverter : TypeConverter<DateTimeOffset?>
{
    public override DateTimeOffset? FromString(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
              ? null
              : DateTimeOffset.ParseExact(
              value,
              "O",
              CultureInfo.InvariantCulture,
              DateTimeStyles.RoundtripKind);
    }

    public override string? ToString(DateTimeOffset? value)
    {
        return value?.ToString("O", CultureInfo.InvariantCulture) ?? null;
    }
}
