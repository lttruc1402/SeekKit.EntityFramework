namespace SeekKit.Example.Api.Data;

/// <summary>
/// Projected shape for <c>GET /products/projected</c> — demonstrates
/// <c>ISeekBuilder&lt;T&gt;.Select&lt;TResult&gt;</c>. Must expose the same sort-column
/// names/types as <see cref="Product"/> (Id, CreatedAt) so SeekKit can read cursor
/// values from the projected shape.
/// </summary>
public class ProductSummary
{
    public long Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
}
