namespace SeekKit.Example.Api.Data;

public class Product
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
}
