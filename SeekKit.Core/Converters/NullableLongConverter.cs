namespace SeekKit.Core.Converters;

public class NullableLongConverter : TypeConverter<long?>
{
    public override string? ToString(long? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? null;
    }

    public override long? FromString(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? null
            : long.Parse(value, CultureInfo.InvariantCulture);
    }
}