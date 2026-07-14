namespace SeekKit.Core.Converters;

internal sealed class CharConverter : TypeConverter<char>
{
    public override string ToString(char value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    public override char FromString(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 1)
            throw new FormatException($"Cannot convert '{value}' to char");

        return value[0];
    }
}