namespace SeekKit.EntityFramework.Models;

/// <summary>
/// Indicates the navigation direction encoded inside a pagination token.
/// </summary>
public enum SeekDirection : byte
{
    /// <summary>Navigate forward — towards newer / larger-keyed records.</summary>
    Next,

    /// <summary>Navigate backward — towards older / smaller-keyed records.</summary>
    Previous
}
