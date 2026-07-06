namespace SeekKit.EntityFramework.Converters;

internal sealed class NullableCharConverter : TypeConverter<char?>
{
    public override string? ToString(char? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture) ?? null;
    }

    public override char? FromString(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        if (value.Length != 1)
            throw new FormatException($"Cannot convert '{value}' to char");

        return value[0];
    }
}