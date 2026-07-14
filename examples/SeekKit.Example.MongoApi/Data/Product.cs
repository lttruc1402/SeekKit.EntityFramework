using MongoDB.Bson;

namespace SeekKit.Example.MongoApi.Data;

public class Product
{
    public ObjectId Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
}
