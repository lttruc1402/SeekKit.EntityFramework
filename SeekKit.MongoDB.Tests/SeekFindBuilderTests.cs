using MongoDB.Driver;
using SeekKit.Core;
using SeekKit.Core.Strategies;

namespace SeekKit.MongoDB.Tests;

/// <summary>
/// CI-safe tests for the find builder. Building an <see cref="IFindFluent{T, T}"/>
/// and configuring the builder do not open a connection. End-to-end paging over a
/// live server is verified separately.
/// </summary>
public sealed class SeekFindBuilderTests
{
    private sealed class Doc
    {
        public ObjectId Id { get; set; }
        public int Rank { get; set; }
    }

    private static ISeekMongoService Service()
    {
        var services = new ServiceCollection();
        services.AddSeekKitMongo(o => o.DefaultPageSize = 10);
        return services.BuildServiceProvider().GetRequiredService<ISeekMongoService>();
    }

    // Creating a find fluent does not connect — safe with a dummy client.
    private static IFindFluent<Doc, Doc> Find()
        => new MongoClient("mongodb://localhost:27017")
            .GetDatabase("test")
            .GetCollection<Doc>("docs")
            .Find(FilterDefinition<Doc>.Empty);

    [Fact]
    public void CreateBuilder_FromFind_ReturnsBuilder()
    {
        Assert.NotNull(Service().CreateBuilder(Find()));
    }

    [Fact]
    public void CreateBuilder_NullFind_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => Service().CreateBuilder((IFindFluent<Doc, Doc>)null!));
    }

    [Fact]
    public async Task SeekAsync_NullArguments_Throw()
    {
        var seek = Service();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            seek.SeekAsync((IFindFluent<Doc, Doc>)null!, new SeekRequest(), b => b.OrderBy(d => d.Id)).AsTask());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            seek.SeekAsync(Find(), null!, b => b.OrderBy(d => d.Id)).AsTask());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            seek.SeekAsync(Find(), new SeekRequest(), null!).AsTask());
    }

    [Fact]
    public void OrderBy_IsChainable()
    {
        var builder = Service().CreateBuilder(Find())
            .WithRequest(new SeekRequest())
            .OrderByDescending(d => d.Rank)
            .OrderBy(d => d.Id);

        Assert.NotNull(builder);
    }
}
