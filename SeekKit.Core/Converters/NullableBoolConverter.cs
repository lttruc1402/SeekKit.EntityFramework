namespace SeekKit.Core.Converters;

internal sealed class NullableBoolConverter : TypeConverter<bool?>
{
    public override string ToString(bool? value)
    {
        return value.HasValue ? (value.Value ? bool.TrueString : bool.FalseString) : string.Empty;
    }

    public override bool? FromString(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? null
            : bool.Parse(value);
    }
}