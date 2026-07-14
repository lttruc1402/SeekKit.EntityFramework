using System.Text.Json;

namespace SeekKit.EntityFramework.Tests.Unit;

file static class Json
{
    // Mirror ASP.NET Core minimal-API serialization (camelCase standard props).
    public static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
}

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
    public void Map_PreservesExtensionData_AndItSerializes()
    {
        var result = Make([1])
            .WithValue("total", 99)
            .WithValue("filter", "active");

        var mapped = result.Map(x => x.ToString());

        Assert.Equal("1", mapped.Items[0]);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(mapped, Json.Web));
        Assert.Equal(99, doc.RootElement.GetProperty("total").GetInt32());
        Assert.Equal("active", doc.RootElement.GetProperty("filter").GetString());
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

    [Fact]
    public void WithValue_SerializesAsTopLevelJsonProperties()
    {
        // Regression: ExtensionData must be public for [JsonExtensionData] to be
        // honored by System.Text.Json — otherwise WithValue data is silently
        // dropped from the response.
        var result = Make([1, 2], nextToken: "next")
            .WithValue("elapsedMs", 3.5)
            .WithValue("totalActive", 42);

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result, Json.Web));
        var root = doc.RootElement;

        // Extra values appear as siblings of the standard properties
        Assert.Equal(3.5, root.GetProperty("elapsedMs").GetDouble());
        Assert.Equal(42, root.GetProperty("totalActive").GetInt32());
        // Standard properties still present
        Assert.Equal("next", root.GetProperty("nextToken").GetString());
        Assert.Equal(2, root.GetProperty("count").GetInt32());
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
