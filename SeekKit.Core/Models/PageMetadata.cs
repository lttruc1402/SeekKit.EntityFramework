namespace SeekKit.Core.Models;

/// <summary>
/// Metadata about the pagination request that produced a <see cref="SeekResult{T}"/>.
/// </summary>
public sealed class PageMetadata
{
    /// <summary>The effective page size used for this request after clamping.</summary>
    public int PageSize { get; init; }

    /// <summary>The UTC timestamp when the query was executed.</summary>
    public DateTime RequestedAt { get; init; }
}
