using MongoDB.Bson;

namespace SeekKit.Example.MongoApi.Data;

public class Category
{
    public ObjectId Id { get; set; }
    public string Name { get; set; } = string.Empty;
}
