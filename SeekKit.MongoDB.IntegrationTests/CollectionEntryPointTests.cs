using SeekKit.Core.Models;
using Xunit;

namespace SeekKit.MongoDB.IntegrationTests;

/// <summary>
/// End-to-end tests for the <see cref="MongoDB.Driver.IMongoCollection{T}"/> entry
/// point against a real MongoDB.
/// </summary>
public sealed class CollectionEntryPointTests : IntegrationTestBase
{
    public CollectionEntryPointTests(MongoFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Collection_ForwardTraversal_CoversAll()
    {
        var col = await Fixture.SeedAsync("coll_all", 33);

        var all = await TraverseForwardAsync(token => Seek.SeekAsync(
            col,
            new SeekRequest { Token = token, PageSize = 10 },
            b => b.OrderBy(p => p.Rank).OrderBy(p => p.Id)).AsTask());

        Assert.Equal(33, all.Count);
        Assert.Equal(Enumerable.Range(1, 33), all.Select(p => p.Rank));
    }

    [Fact]
    public async Task Collection_FirstPage_HasCorrectFlagsAndTokens()
    {
        var col = await Fixture.SeedAsync("coll_flags", 25);

        var first = await Seek.SeekAsync(
            col,
            new SeekRequest { PageSize = 10 },
            b => b.OrderBy(p => p.Rank).OrderBy(p => p.Id));

        Assert.Equal(10, first.Count);
        Assert.True(first.HasNext);
        Assert.False(first.HasPrevious);
        Assert.NotNull(first.NextToken);
        Assert.Null(first.PreviousToken);
    }

    [Fact]
    public async Task EmptyCollection_ReturnsEmptyPage()
    {
        var col = await Fixture.SeedAsync("coll_empty", 0);

        var result = await Seek.SeekAsync(
            col,
            new SeekRequest { PageSize = 10 },
            b => b.OrderBy(p => p.Rank).OrderBy(p => p.Id));

        Assert.Empty(result.Items);
        Assert.False(result.HasNext);
        Assert.Null(result.NextToken);
    }
}
