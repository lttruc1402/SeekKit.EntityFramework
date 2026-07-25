namespace SeekKit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// Projected shape used in Select() tests. Exposes Id/CreatedAt (the sort columns used
/// in these tests) plus CategoryName, which only exists after the push-down join.
/// </summary>
public sealed class ProductSummaryDto
{
    public int      Id           { get; set; }
    public DateTime CreatedAt    { get; set; }
    public string   CategoryName { get; set; } = "";
}
