namespace SeekKit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// Minimal test entity used across all integration tests.
/// </summary>
public sealed class Product
{
    public int      Id        { get; set; }
    public string   Name      { get; set; } = "";
    public decimal  Price     { get; set; }
    public DateTime CreatedAt { get; set; }
}
