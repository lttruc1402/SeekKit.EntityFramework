namespace SeekKit.Core.Converters;

internal class NullableIntConverter : TypeConverter<int?>
{
    public override int? FromString(string? value)
    {
        if(string.IsNullOrWhiteSpace(value))
            return null;

        return int.Parse(value);
    }

    public override string? ToString(int? value)
    {
        if (value == null) return null;
        return value.Value.ToString();
    }
}
