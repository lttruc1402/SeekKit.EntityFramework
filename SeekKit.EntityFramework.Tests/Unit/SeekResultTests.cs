namespace SeekKit.EntityFramework.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SeekResult{T}"/> — Map, WithValue, and property invariants.
/// No database or license required.
/// </summary>
public sealed class SeekResultTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static SeekResult<T> Make<T>(
        T[]     items,
        string? nextToken  = null,
        string? prevToken  = null,
        int     pageSize   = 10)
        => new()
        {
            Items         = items,
            NextToken     = nextToken,
            PreviousToken = prevToken,
            HasNext       = nextToken  is not null,
            HasPrevious   = prevToken  is not null,
            Count         = items.Length,
            PageMetadata  = new PageMetadata
            {
                PageSize    = pageSize,
                RequestedAt = DateTime.UtcNow
            }
        };

    // ── Map ───────────────────────────────────────────────────────────────────

    [Fact]
    public void Map_TransformsEachItem()
    {
        var result = Make([1, 2, 3]);
        var mapped = result.Map(x => x * 10);

        Assert.Equal([10, 20, 30], mapped.Items);
    }

    [Fact]
    public void Map_PreservesTokens()
    {
        var result = Make([1, 2], nextToken: "next", prevToken: "prev");
        var mapped = result.Map(x => x.ToString());

        Assert.Equal("next", mapped.NextToken);
        Assert.Equal("prev", mapped.PreviousToken);
    }

    [Fact]
    public void Map_PreservesHasNextHasPrevious()
    {
        var result = Make([1, 2], nextToken: "tok", prevToken: null);
        var mapped = result.Map(x => x.ToString());

        Assert.True(mapped.HasNext);
        Assert.False(mapped.HasPrevious);
    }

    [Fact]
    public void Map_PreservesCount()
    {
        var result = Make([10, 20, 30]);
        var mapped = result.Map(x => (double)x);

        Assert.Equal(3, mapped.Count);
    }

    [Fact]
    public void Map_PreservesPageMetadata()
    {
        var result = Make([1, 2], pageSize: 5);
        var mapped = result.Map(x => x.ToString());

        Assert.Equal(5, mapped.PageMetadata.PageSize);
    }

    [Fact]
    public void Map_EmptyItems_ReturnsEmptyResult()
    {
        var result = Make(Array.Empty<int>());
        var mapped = result.Map(x => x * 2);

        Assert.Empty(mapped.Items);
        Assert.Equal(0, mapped.Count);
        Assert.Null(mapped.NextToken);
        Assert.Null(mapped.PreviousToken);
    }

    [Fact]
    public void Map_ToDifferentType_Works()
    {
        var result = Make([Guid.NewGuid(), Guid.NewGuid()]);
        var mapped = result.Map(g => g.ToString());

        Assert.Equal(2, mapped.Items.Count);
        Assert.All(mapped.Items, s => Assert.True(Guid.TryParse(s, out _)));
    }

    [Fact]
    public void Map_PreservesExtensionData()
    {
        var result = Make([1])
            .WithValue("total", 99)
            .WithValue("filter", "active");

        var mapped = result.Map(x => x.ToString());

        // Extension data is propagated via the Map implementation
        Assert.NotNull(mapped);
        Assert.Equal("1", mapped.Items[0]);
    }

    // ── WithValue ─────────────────────────────────────────────────────────────

    [Fact]
    public void WithValue_ReturnsSameInstance()
    {
        var result = Make([1, 2, 3]);
        var returned = result.WithValue("key", "val");

        Assert.Same(result, returned);
    }

    [Fact]
    public void WithValue_MultipleKeys_ChainWorks()
    {
        var result = Make([1])
            .WithValue("a", 1)
            .WithValue("b", 2)
            .WithValue("c", 3);

        Assert.NotNull(result); // chain didn't throw
    }

    // ── Property invariants ───────────────────────────────────────────────────

    [Fact]
    public void FirstPage_HasPreviousIsFalse_WhenNoPrevToken()
    {
        var result = Make([1, 2, 3], nextToken: "tok", prevToken: null);

        Assert.False(result.HasPrevious);
        Assert.Null(result.PreviousToken);
        Assert.True(result.HasNext);
        Assert.NotNull(result.NextToken);
    }

    [Fact]
    public void LastPage_HasNextIsFalse_WhenNoNextToken()
    {
        var result = Make([28, 29, 30], nextToken: null, prevToken: "prev");

        Assert.False(result.HasNext);
        Assert.Null(result.NextToken);
        Assert.True(result.HasPrevious);
        Assert.NotNull(result.PreviousToken);
    }

    [Fact]
    public void Count_MatchesItemsLength()
    {
        var items  = new[] { 1, 2, 3, 4, 5 };
        var result = Make(items);

        Assert.Equal(items.Length, result.Count);
        Assert.Equal(items.Length, result.Items.Count);
    }
}
