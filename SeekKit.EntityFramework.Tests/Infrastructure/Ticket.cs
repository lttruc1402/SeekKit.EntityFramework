namespace SeekKit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// Test entity with nullable sort columns of every flavour: a nullable value type, a
/// nullable reference type, a nullable date, and a non-null string with duplicates.
/// </summary>
public sealed class Ticket
{
    public int       Id       { get; set; }
    public string    Title    { get; set; } = "";
    public string?   Assignee { get; set; }
    public int?      Priority { get; set; }
    public DateTime? DueAt    { get; set; }
}
