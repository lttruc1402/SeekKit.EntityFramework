namespace SeekKit.Core.Converters;

internal sealed class NullableGuidConverter : TypeConverter<Guid?>
{
    public override string? ToString(Guid? value)
    {
        return value?.ToString("D") ?? null;
    }

    public override Guid? FromString(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? null
            : Guid.ParseExact(value, "D");
    }
}