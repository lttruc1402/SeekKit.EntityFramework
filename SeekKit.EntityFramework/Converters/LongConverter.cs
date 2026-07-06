
namespace SeekKit.EntityFramework.Converters;

internal sealed class LongConverter : TypeConverter<long>
{

    public override string? ToString(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public override long FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
        return long.Parse(value, CultureInfo.InvariantCulture);
    }
}