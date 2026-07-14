using MongoDB.Bson;
using MongoDB.Driver;
using Testcontainers.MongoDb;
using Xunit;

namespace SeekKit.MongoDB.IntegrationTests;

/// <summary>
/// Starts a real MongoDB in a throwaway container (via Testcontainers) once for
/// all tests in the collection. This exercises the actual driver query
/// translation ($or, $sort, $unionWith, $match, $and) that the in-memory unit
/// tests cannot reach — the point where runtime translation errors surface.
/// </summary>
public sealed class MongoFixture : IAsyncLifetime
{
    private readonly MongoDbContainer _container = new MongoDbBuilder("mongo:7")
        .Build();

    public IMongoClient Client { get; private set; } = null!;
    public IMongoDatabase Database { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        Client = new MongoClient(_container.GetConnectionString());
        Database = Client.GetDatabase("seekkit_it");
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Creates a fresh collection seeded with <paramref name="count"/> ordered docs.</summary>
    public async Task<IMongoCollection<Product>> SeedAsync(string name, int count)
    {
        await Database.DropCollectionAsync(name);
        var col = Database.GetCollection<Product>(name);

        var baseTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var docs = new List<Product>(count);
        for (int i = 1; i <= count; i++)
        {
            docs.Add(new Product
            {
                Id        = ObjectId.GenerateNewId(),
                Name      = $"P{i}",
                Rank      = i,
                Price     = i * 1.5m,
                CreatedAt = baseTime.AddMinutes(i),
                IsActive  = i % 5 != 0,   // every 5th inactive
            });
        }
        if (docs.Count > 0)                     // InsertMany throws on an empty list
            await col.InsertManyAsync(docs);

        await col.Indexes.CreateOneAsync(new CreateIndexModel<Product>(
            Builders<Product>.IndexKeys.Descending(p => p.CreatedAt).Ascending(p => p.Id)));

        return col;
    }
}

public sealed class Product
{
    public ObjectId Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Rank { get; set; }
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; }
}

[CollectionDefinition("mongo")]
public sealed class MongoCollection : ICollectionFixture<MongoFixture> { }
