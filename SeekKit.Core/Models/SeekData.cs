namespace SeekKit.Core.Models;

/// <summary>
/// The decoded payload of a pagination token. Contains the navigation direction
/// and the keyset values of the boundary record. This type is serialized into the
/// opaque token returned to clients.
/// </summary>
public sealed class SeekData
{
    /// <summary>Whether this token represents forward or backward navigation.</summary>
    public SeekDirection Direction { get; set; }

    /// <summary>
    /// The keyset values of the boundary record, keyed by order-field name.
    /// Each value is stored as its string representation for serialization. A value can be
    /// <see langword="null"/> despite the declared type: that is how a boundary record whose sort
    /// value was <c>NULL</c> is represented.
    /// </summary>
    public Dictionary<string, string> Values { get; set; } = [];

    /// <summary>
    /// A short fingerprint of the sort (columns, their order, their directions) the token
    /// was issued for. A token whose fingerprint differs from the current query's is
    /// rejected with <see cref="SeekTokenException"/>. Absent on tokens issued before 2.4.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Sort { get; set; }

    /// <summary>
    /// When the token stops being valid, in Unix seconds. Present only when
    /// <see cref="SeekOptionsBase.TokenLifetime"/> is configured.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ExpiresAt { get; set; }

    /// <summary>
    /// Where the target database places <c>NULL</c> when sorting. Not part of the token —
    /// filled in from the options when the token is decoded, so that an
    /// <see cref="ISeekFilterStrategy"/> can build a null-aware keyset filter.
    /// </summary>
    [JsonIgnore]
    public SeekNullOrdering NullOrdering { get; set; }
}
