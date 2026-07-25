namespace SeekKit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// Related entity used to exercise Select()'s push-down join in projection tests.
/// </summary>
public sealed class Category
{
    public int    Id   { get; set; }
    public string Name { get; set; } = "";
}
