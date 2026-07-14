namespace SeekKit.Core.Models;

/// <summary>
/// Represents an incoming pagination request from the client.
/// Pass <see cref="Token"/> from a previous <see cref="SeekResult{T}"/> to navigate pages.
/// </summary>
public class SeekRequest
{
    /// <summary>
    /// The opaque pagination token received from <see cref="SeekResult{T}.NextToken"/> or
    /// <see cref="SeekResult{T}.PreviousToken"/>. Set to <c>null</c> or omit to start from
    /// the first page.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Number of items to return per page. When <c>null</c>, falls back to
    /// <see cref="SeekOptionsBase.DefaultPageSize"/>. Clamped between
    /// <see cref="SeekOptionsBase.MinPageSize"/> and <see cref="SeekOptionsBase.MaxPageSize"/>.
    /// </summary>
    public int? PageSize { get; set; }
}
