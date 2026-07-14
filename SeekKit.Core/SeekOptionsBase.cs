namespace SeekKit.Core;

/// <summary>
/// Provider-agnostic pagination options shared by all SeekKit provider packages
/// (SeekKit.EntityFramework, SeekKit.MongoDB, ...). Provider packages derive
/// their own options type from this class.
/// </summary>
public abstract class SeekOptionsBase
{
    protected SeekOptionsBase() { }

    protected SeekOptionsBase(SeekOptionsBase options)
    {
        DefaultPageSize = options.DefaultPageSize;
        MaxPageSize     = options.MaxPageSize;
        MinPageSize     = options.MinPageSize;
    }

    /// <summary>
    /// Number of items per page when the client does not specify <see cref="SeekRequest.PageSize"/>.
    /// Default: <c>10</c>.
    /// </summary>
    public int DefaultPageSize { get; set; } = 10;

    /// <summary>
    /// Upper bound for <see cref="SeekRequest.PageSize"/>. Requests above this value are clamped.
    /// Default: <c>1000</c>.
    /// </summary>
    public int MaxPageSize { get; set; } = 1000;

    /// <summary>
    /// Lower bound for <see cref="SeekRequest.PageSize"/>. Requests below this value are clamped.
    /// Default: <c>1</c>.
    /// </summary>
    public int MinPageSize { get; set; } = 1;
}
