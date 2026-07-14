using MongoDB.Driver;
using SeekKit.Core;
using SeekKit.Core.Strategies;

namespace SeekKit.MongoDB.Tests;

/// <summary>
/// CI-safe tests for the aggregation-pipeline builder. Building an
/// <see cref="IAggregateFluent{T}"/> and configuring the builder do not open a
/// connection, so these run without a live MongoDB. End-to-end paging over a
/// real <c>$unionWith</c> pipeline is verified against a live server separately.
/// </summary>
public sealed class SeekAggregateBuilderTests
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

    // Creating an aggregate fluent does not connect — safe with a dummy client.
    private static IAggregateFluent<Doc> Pipeline()
        => new MongoClient("mongodb://localhost:27017")
            .GetDatabase("test")
            .GetCollection<Doc>("docs")
            .Aggregate();

    [Fact]
    public void CreateBuilder_FromAggregate_ReturnsBuilder()
    {
        var builder = Service().CreateBuilder(Pipeline());
        Assert.NotNull(builder);
    }

    [Fact]
    public void CreateBuilder_NullAggregate_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => Service().CreateBuilder((IAggregateFluent<Doc>)null!));
    }

    [Fact]
    public void WithStrategy_OnAggregateBuilder_Throws()
    {
        var builder = Service().CreateBuilder(Pipeline());
        Assert.Throws<NotSupportedException>(
            () => builder.WithStrategy(new OrLogicSeekStrategy(
                new ServiceCollection().AddSeekKitMongo().BuildServiceProvider()
                    .GetRequiredService<ISeekValueConverter>())));
    }

    [Fact]
    public async Task SeekAsync_NullArguments_Throw()
    {
        var seek = Service();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            seek.SeekAsync((IAggregateFluent<Doc>)null!, new SeekRequest(), b => b.OrderBy(d => d.Id)).AsTask());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            seek.SeekAsync(Pipeline(), null!, b => b.OrderBy(d => d.Id)).AsTask());

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            seek.SeekAsync(Pipeline(), new SeekRequest(), null!).AsTask());
    }

    [Fact]
    public void OrderBy_IsChainable()
    {
        var builder = Service().CreateBuilder(Pipeline())
            .WithRequest(new SeekRequest())
            .OrderByDescending(d => d.Rank)
            .OrderBy(d => d.Id);

        Assert.NotNull(builder);
    }
}
