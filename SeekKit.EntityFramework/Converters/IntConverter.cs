namespace SeekKit.EntityFramework.Converters;

internal sealed class IntConverter : TypeConverter<int>
{
    public override int FromString(string? value)
    {
      if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(value));

       return int.Parse(value);
    }

    public override string? ToString(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
