using MongoDB.Bson;
using MongoDB.Driver;
using SeekKit.Core.Models;
using Xunit;

namespace SeekKit.MongoDB.IntegrationTests;

/// <summary>
/// End-to-end tests for the <see cref="IAggregateFluent{T}"/> entry point against
/// a real MongoDB — exercises SeekKit's appended keyset <c>$match</c> + <c>$sort</c>
/// + <c>$limit</c> on top of a <c>$unionWith</c> pipeline.
/// </summary>
public sealed class AggregateEntryPointTests : IntegrationTestBase
{
    public AggregateEntryPointTests(MongoFixture fixture) : base(fixture) { }

    private async Task<(IMongoCollection<Product> a, IMongoCollection<Product> b)> SeedDisjointAsync()
    {
        var a = await Fixture.SeedAsync("agg_a", 0);
        var b = await Fixture.SeedAsync("agg_b", 0);
        var baseTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var da = new List<Product>(); var dbb = new List<Product>();
        for (int i = 1; i <= 40; i++)
        {
            var p = new Product { Id = ObjectId.GenerateNewId(), Name = $"P{i}", Rank = i, Price = i, CreatedAt = baseTime.AddMinutes(i), IsActive = true };
            (i % 2 == 1 ? da : dbb).Add(p);
        }
        await a.InsertManyAsync(da);
        await b.InsertManyAsync(dbb);
        return (a, b);
    }

    [Fact]
    public async Task UnionWith_ForwardTraversal_CoversAll()
    {
        var (a, b) = await SeedDisjointAsync();

        var all = await TraverseForwardAsync(token => Seek.SeekAsync(
            a.Aggregate().UnionWith(b),
            new SeekRequest { Token = token, PageSize = 7 },
            bd => bd.OrderBy(p => p.Rank).OrderBy(p => p.Id)).AsTask());

        Assert.Equal(40, all.Count);
        Assert.Equal(Enumerable.Range(1, 40), all.Select(p => p.Rank));
    }

    [Fact]
    public async Task UnionWith_DescendingThenBackward_ReturnsToFirstPage()
    {
        var (a, b) = await SeedDisjointAsync();

        Task<SeekResult<Product>> Page(string? t) => Seek.SeekAsync(
            a.Aggregate().UnionWith(b),
            new SeekRequest { Token = t, PageSize = 10 },
            bd => bd.OrderByDescending(p => p.Rank).OrderBy(p => p.Id)).AsTask();

        var p1 = await Page(null);
        var p2 = await Page(p1.NextToken);
        var back = await Page(p2.PreviousToken);

        Assert.Equal(40, p1.Items[0].Rank);
        Assert.Equal(31, p1.Items[^1].Rank);
        Assert.Equal(p1.Items.Select(x => x.Rank), back.Items.Select(x => x.Rank));
    }

    [Fact]
    public async Task Pipeline_WithMatchStage_KeepsBaseFilter()
    {
        var (a, b) = await SeedDisjointAsync();

        // pre-$match keeps only rank > 25 on each branch → union 26..40
        var all = await TraverseForwardAsync(token => Seek.SeekAsync(
            a.Aggregate().Match(x => x.Rank > 25).UnionWith(
                b, PipelineDefinition<Product, Product>.Create(
                    new[] { new BsonDocument("$match", new BsonDocument("Rank", new BsonDocument("$gt", 25))) })),
            new SeekRequest { Token = token, PageSize = 6 },
            bd => bd.OrderBy(p => p.Rank).OrderBy(p => p.Id)).AsTask());

        Assert.Equal(Enumerable.Range(26, 15), all.Select(p => p.Rank));
    }
}
