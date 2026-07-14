namespace SeekKit.Core.Converters;

internal sealed class DelegateTypeConverter<T> : TypeConverter<T>
{
    private readonly Func<string?, T> _fromString;
    private readonly Func<T, string?> _toString;

    internal DelegateTypeConverter(Func<T, string?> toString, Func<string?, T> fromString)
    {
        _toString = toString ?? throw new ArgumentNullException(nameof(toString));
        _fromString = fromString ?? throw new ArgumentNullException(nameof(fromString));
    }
    public override T FromString(string? value)
    {
        return _fromString(value);
    }

    public override string? ToString(T value)
    {
        return _toString(value);
    }
}
