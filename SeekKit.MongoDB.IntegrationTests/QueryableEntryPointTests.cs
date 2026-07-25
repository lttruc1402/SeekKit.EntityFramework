using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using SeekKit.Core.Models;
using Xunit;

namespace SeekKit.MongoDB.IntegrationTests;

/// <summary>
/// End-to-end tests for the <see cref="IQueryable{T}"/> entry point against a
/// real MongoDB — exercises the driver's LINQ → <c>$match</c>/<c>$sort</c>
/// translation of the keyset predicate.
/// </summary>
public sealed class QueryableEntryPointTests : IntegrationTestBase
{
    public QueryableEntryPointTests(MongoFixture fixture) : base(fixture) { }

    [Fact]
    public async Task ForwardTraversal_CoversAll_NoGapsNoDuplicates()
    {
        var col = await Fixture.SeedAsync("q_forward", 47);

        var all = await TraverseForwardAsync(token => Seek.SeekAsync(
            col.AsQueryable(),
            new SeekRequest { Token = token, PageSize = 8 },
            b => b.OrderBy(p => p.Rank).OrderBy(p => p.Id)).AsTask());

        Assert.Equal(47, all.Count);
        Assert.Equal(Enumerable.Range(1, 47), all.Select(p => p.Rank));
    }

    [Fact]
    public async Task DescendingByDateTime_ThenBackward_ReturnsToFirstPage()
    {
        var col = await Fixture.SeedAsync("q_desc", 30);

        Task<SeekResult<Product>> Page(string? t) => Seek.SeekAsync(
            col.AsQueryable(),
            new SeekRequest { Token = t, PageSize = 10 },
            b => b.OrderByDescending(p => p.CreatedAt).OrderBy(p => p.Id)).AsTask();

        var p1 = await Page(null);
        var p2 = await Page(p1.NextToken);
        var back = await Page(p2.PreviousToken);

        Assert.Equal(30, p1.Items[0].Rank);   // newest first
        Assert.Equal(21, p1.Items[^1].Rank);
        Assert.Equal(20, p2.Items[0].Rank);
        Assert.Equal(p1.Items.Select(x => x.Rank), back.Items.Select(x => x.Rank));
    }

    [Fact]
    public async Task MixedDirections_TranslateAndOrderCorrectly()
    {
        var col = await Fixture.SeedAsync("q_mixed", 25);

        var all = await TraverseForwardAsync(token => Seek.SeekAsync(
            col.AsQueryable(),
            new SeekRequest { Token = token, PageSize = 7 },
            b => b.OrderByDescending(p => p.Price).OrderBy(p => p.Id)).AsTask());

        Assert.Equal(25, all.Count);
        Assert.Equal(all.Select(p => p.Price).OrderByDescending(x => x), all.Select(p => p.Price));
    }

    [Fact]
    public async Task PreFilteredWhere_PaginatesOnlyMatches()
    {
        var col = await Fixture.SeedAsync("q_filter", 40);

        var all = await TraverseForwardAsync(token => Seek.SeekAsync(
            col.AsQueryable().Where(p => p.IsActive),
            new SeekRequest { Token = token, PageSize = 9 },
            b => b.OrderBy(p => p.Rank).OrderBy(p => p.Id)).AsTask());

        Assert.All(all, p => Assert.True(p.IsActive));
        Assert.DoesNotContain(all, p => p.Rank % 5 == 0);   // inactive ones excluded
    }

    [Fact]
    public async Task Union_TwoCollections_MergesAndOrders()
    {
        // odd ranks in A, even in B → union = 1..40
        var a = await Fixture.SeedAsync("q_union_a", 0);
        var b = await Fixture.SeedAsync("q_union_b", 0);
        var baseTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var da = new List<Product>(); var dbb = new List<Product>();
        for (int i = 1; i <= 40; i++)
        {
            var p = new Product { Id = ObjectId.GenerateNewId(), Name = $"P{i}", Rank = i, Price = i, CreatedAt = baseTime.AddMinutes(i), IsActive = true };
            (i % 2 == 1 ? da : dbb).Add(p);
        }
        await a.InsertManyAsync(da);
        await b.InsertManyAsync(dbb);

        var all = await TraverseForwardAsync(token => Seek.SeekAsync(
            a.AsQueryable().Union(b.AsQueryable()),
            new SeekRequest { Token = token, PageSize = 8 },
            bd => bd.OrderBy(p => p.Rank).OrderBy(p => p.Id)).AsTask());

        Assert.Equal(40, all.Count);
        Assert.Equal(Enumerable.Range(1, 40), all.Select(p => p.Rank));
    }

    [Fact]
    public async Task Select_ForwardTraversal_MatchesNonProjectedOrder()
    {
        var collection = await Fixture.SeedAsync("queryable_select", 30);

        var expected = await TraverseForwardAsync(token => Seek.SeekAsync(
            collection.AsQueryable(),
            new SeekRequest { Token = token, PageSize = 7 },
            b => b.OrderBy(p => p.Rank).OrderBy(p => p.Id)).AsTask());

        var allNames = new List<string>();
        string? projToken = null;
        int guard = 0;
        do
        {
            var page = await Seek
                .CreateBuilder(collection.AsQueryable())
                .WithRequest(new SeekRequest { Token = projToken, PageSize = 7 })
                .OrderBy(p => p.Rank)
                .OrderBy(p => p.Id)
                .Select(q => q.Select(p => new { p.Id, p.Rank, Display = p.Name.ToUpper() }))
                .ToSeekResultAsync();

            allNames.AddRange(page.Items.Select(x => x.Display));
            projToken = page.NextToken;
            if (++guard > 1000) throw new Exception("runaway pagination");
        } while (projToken != null);

        Assert.Equal(expected.Select(p => p.Rank), Enumerable.Range(1, 30));
        Assert.Equal(30, allNames.Count);
        Assert.All(allNames, n => Assert.Equal(n, n.ToUpper()));
    }

    [Fact]
    public async Task Concat_IsKnownToFail_DocumentedLimitation()
    {
        // Documents why the library uses .Union() not .Concat(): the LINQ provider
        // throws on Concat + OrderBy + Take. This test locks that expectation so a
        // future driver change that fixes it is noticed.
        var a = await Fixture.SeedAsync("q_concat_a", 5);
        var b = await Fixture.SeedAsync("q_concat_b", 5);

        await Assert.ThrowsAnyAsync<Exception>(() => Seek.SeekAsync(
            a.AsQueryable().Concat(b.AsQueryable()),
            new SeekRequest { PageSize = 4 },
            bd => bd.OrderBy(p => p.Rank).OrderBy(p => p.Id)).AsTask());
    }
}
