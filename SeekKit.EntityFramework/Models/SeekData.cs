namespace SeekKit.EntityFramework.Models;

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
    /// Each value is stored as its string representation for serialization.
    /// </summary>
    public Dictionary<string, string> Values { get; set; } = [];
}
