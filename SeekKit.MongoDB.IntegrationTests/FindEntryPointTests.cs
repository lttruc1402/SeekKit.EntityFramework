using MongoDB.Driver;
using SeekKit.Core.Models;
using Xunit;

namespace SeekKit.MongoDB.IntegrationTests;

/// <summary>
/// End-to-end tests for the <see cref="IFindFluent{T, T}"/> entry point against a
/// real MongoDB — exercises SeekKit AND-ing the keyset predicate into a BSON
/// <see cref="FilterDefinition{T}"/> query filter.
/// </summary>
public sealed class FindEntryPointTests : IntegrationTestBase
{
    public FindEntryPointTests(MongoFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Find_Empty_ForwardTraversal_CoversAll()
    {
        var col = await Fixture.SeedAsync("find_all", 35);

        var all = await TraverseForwardAsync(token => Seek.SeekAsync(
            col.Find(FilterDefinition<Product>.Empty),
            new SeekRequest { Token = token, PageSize = 8 },
            b => b.OrderBy(p => p.Rank).OrderBy(p => p.Id)).AsTask());

        Assert.Equal(35, all.Count);
        Assert.Equal(Enumerable.Range(1, 35), all.Select(p => p.Rank));
    }

    [Fact]
    public async Task Find_BsonFilter_PreservedAcrossEveryPage()
    {
        var col = await Fixture.SeedAsync("find_filter", 40);

        // base filter Rank >= 15 must persist on page 2+ (where the keyset $and applies)
        var filter = Builders<Product>.Filter.Gte(p => p.Rank, 15);

        var all = await TraverseForwardAsync(token => Seek.SeekAsync(
            col.Find(filter),
            new SeekRequest { Token = token, PageSize = 6 },
            b => b.OrderBy(p => p.Rank).OrderBy(p => p.Id)).AsTask());

        Assert.Equal(Enumerable.Range(15, 26), all.Select(p => p.Rank));
        Assert.All(all, p => Assert.True(p.Rank >= 15));
    }

    [Fact]
    public async Task Find_DescendingThenBackward_ReturnsToFirstPage()
    {
        var col = await Fixture.SeedAsync("find_desc", 30);

        Task<SeekResult<Product>> Page(string? t) => Seek.SeekAsync(
            col.Find(FilterDefinition<Product>.Empty),
            new SeekRequest { Token = t, PageSize = 10 },
            b => b.OrderByDescending(p => p.CreatedAt).OrderBy(p => p.Id)).AsTask();

        var p1 = await Page(null);
        var p2 = await Page(p1.NextToken);
        var back = await Page(p2.PreviousToken);

        Assert.Equal(30, p1.Items[0].Rank);
        Assert.Equal(p1.Items.Select(x => x.Rank), back.Items.Select(x => x.Rank));
    }

    [Fact]
    public async Task Select_ForwardTraversal_AppliesProjection()
    {
        var collection = await Fixture.SeedAsync("find_select", 25);

        var allDisplays = new List<string>();
        string? token = null;
        int guard = 0;
        do
        {
            var page = await Seek
                .CreateBuilder(collection.Find(FilterDefinition<Product>.Empty))
                .WithRequest(new SeekRequest { Token = token, PageSize = 6 })
                .OrderBy(p => p.Rank)
                .OrderBy(p => p.Id)
                .Select(f => f.Project(Builders<Product>.Projection.Expression(p => new
                {
                    p.Id,
                    p.Rank,
                    Display = p.Name.ToUpper()
                })))
                .ToSeekResultAsync();

            allDisplays.AddRange(page.Items.Select(x => x.Display));
            token = page.NextToken;
            if (++guard > 1000) throw new Exception("runaway pagination");
        } while (token != null);

        Assert.Equal(25, allDisplays.Count);
        Assert.Equal(25, allDisplays.Distinct().Count());
    }
}
