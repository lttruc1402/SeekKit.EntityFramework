namespace SeekKit.Core.Converters;

internal sealed class StringConverter : TypeConverter<string>
{
    public override string FromString(string? value) => value!;

    public override string? ToString(string value) => value!;
}
