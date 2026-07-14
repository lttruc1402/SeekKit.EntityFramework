namespace SeekKit.Core;

/// <summary>
/// Interface for strategies that need page size for filtering
/// (e.g., UNION ALL with TOP N per branch)
/// </summary>
public interface IPageSizeAware
{
    /// <summary>
    /// Set page size for the strategy
    /// Called by builder before ApplyFilter
    /// </summary>
    void SetPageSize(int pageSize);
}