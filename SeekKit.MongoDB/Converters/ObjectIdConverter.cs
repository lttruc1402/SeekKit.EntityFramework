namespace SeekKit.MongoDB.Converters;

/// <summary>
/// Cursor-token converter for MongoDB's <see cref="ObjectId"/> — registered
/// automatically by <c>AddSeekKitMongo</c> so <c>_id</c> can be used as the
/// unique tie-breaker column.
/// </summary>
internal sealed class ObjectIdConverter : TypeConverter<ObjectId>
{
    public override string? ToString(ObjectId value) => value.ToString();

    public override ObjectId FromString(string? value)
        => string.IsNullOrEmpty(value) ? ObjectId.Empty : ObjectId.Parse(value);
}

/// <summary>
/// Cursor-token converter for <see cref="Nullable{ObjectId}"/>.
/// </summary>
internal sealed class NullableObjectIdConverter : TypeConverter<ObjectId?>
{
    public override string? ToString(ObjectId? value) => value?.ToString();

    public override ObjectId? FromString(string? value)
        => string.IsNullOrEmpty(value) ? null : ObjectId.Parse(value);
}
