namespace SeekKit.Core.Converters;

internal sealed class GuidConverter : TypeConverter<Guid>
{
    public override string? ToString(Guid value)
    {
        return value.ToString("D"); // xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
    }

    public override Guid FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
        return Guid.ParseExact(value, "D");
    }
}