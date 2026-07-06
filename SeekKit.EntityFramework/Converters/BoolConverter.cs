namespace SeekKit.EntityFramework.Converters;

internal sealed class BoolConverter : TypeConverter<bool>
{
    public override string ToString(bool value)
    {
        return value ? "true" : "false";
    }

    public override bool FromString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));
        return bool.Parse(value);
    }
}