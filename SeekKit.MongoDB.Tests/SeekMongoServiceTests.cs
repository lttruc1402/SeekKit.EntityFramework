namespace SeekKit.MongoDB.Tests;

/// <summary>
/// Tests for <see cref="ISeekMongoService"/> running the full pagination
/// pipeline (ordering, OR-logic keyset filter, bidirectional tokens,
/// ObjectId converter) over an in-memory queryable. The expression trees
/// exercised here are exactly what the MongoDB LINQ provider receives.
/// </summary>
public sealed class SeekMongoServiceTests
{
    private sealed class Doc
    {
        public ObjectId Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private static ISeekMongoService CreateService(int defaultPageSize = 10, int maxPageSize = 100)
    {
        var services = new ServiceCollection();
        services.AddSeekKitMongo(o =>
        {
            o.DefaultPageSize = defaultPageSize;
            o.MinPageSize     = 1;
            o.MaxPageSize     = maxPageSize;
        });
        return services.BuildServiceProvider().GetRequiredService<ISeekMongoService>();
    }

    private static List<Doc> SeedDocs(int count)
    {
        var baseTime = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var docs = new List<Doc>(count);
        for (int i = 1; i <= count; i++)
        {
            docs.Add(new Doc
            {
                Id        = new ObjectId($"{i:x24}"),
                Name      = $"Doc {i}",
                Price     = i * 1.5m,
                CreatedAt = baseTime.AddMinutes(i),
            });
        }
        return docs;
    }

    // ── First page ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FirstPage_DefaultPageSize_ReturnsExpectedItems()
    {
        var service = CreateService();
        var docs    = SeedDocs(30);

        var result = await service.SeekAsync(
            docs.AsQueryable(),
            new SeekRequest(),
            b => b.OrderBy(d => d.CreatedAt).OrderBy(d => d.Id));

        Assert.Equal(10, result.Count);
        Assert.Equal("Doc 1", result.Items[0].Name);
        Assert.True(result.HasNext);
        Assert.False(result.HasPrevious);
        Assert.NotNull(result.NextToken);
        Assert.Null(result.PreviousToken);
    }

    [Fact]
    public async Task DescendingOrder_FirstPage_StartsFromNewest()
    {
        var service = CreateService();
        var docs    = SeedDocs(30);

        var result = await service.SeekAsync(
            docs.AsQueryable(),
            new SeekRequest { PageSize = 5 },
            b => b.OrderByDescending(d => d.CreatedAt).OrderBy(d => d.Id));

        Assert.Equal("Doc 30", result.Items[0].Name);
        Assert.Equal("Doc 26", result.Items[^1].Name);
    }

    // ── Forward traversal ─────────────────────────────────────────────────────

    [Fact]
    public async Task ForwardTraversal_CoversAllItems_NoGapsNoDuplicates()
    {
        var service = CreateService(defaultPageSize: 7);
        var docs    = SeedDocs(30);

        var seen = new List<string>();
        string? token = null;

        do
        {
            var page = await service.SeekAsync(
                docs.AsQueryable(),
                new SeekRequest { Token = token },
                b => b.OrderBy(d => d.CreatedAt).OrderBy(d => d.Id));

            seen.AddRange(page.Items.Select(d => d.Name));
            token = page.NextToken;
        } while (token != null);

        Assert.Equal(30, seen.Count);
        Assert.Equal(30, seen.Distinct().Count());
        Assert.Equal("Doc 1",  seen[0]);
        Assert.Equal("Doc 30", seen[^1]);
    }

    // ── Backward traversal ────────────────────────────────────────────────────

    [Fact]
    public async Task PreviousToken_NavigatesBackToFirstPage()
    {
        var service = CreateService(defaultPageSize: 10);
        var docs    = SeedDocs(30);

        Task<SeekResult<Doc>> GetPage(string? token) =>
            service.SeekAsync(
                docs.AsQueryable(),
                new SeekRequest { Token = token },
                b => b.OrderBy(d => d.CreatedAt).OrderBy(d => d.Id)).AsTask();

        var page1 = await GetPage(null);
        var page2 = await GetPage(page1.NextToken);

        Assert.Equal("Doc 11", page2.Items[0].Name);
        Assert.True(page2.HasPrevious);

        var page1Again = await GetPage(page2.PreviousToken);

        Assert.Equal("Doc 1",  page1Again.Items[0].Name);
        Assert.Equal("Doc 10", page1Again.Items[^1].Name);
    }

    // ── ObjectId tie-breaker ──────────────────────────────────────────────────

    [Fact]
    public async Task ObjectIdSort_RoundTripsThroughToken()
    {
        var service = CreateService(defaultPageSize: 12);
        var docs    = SeedDocs(25);

        var page1 = await service.SeekAsync(
            docs.AsQueryable(),
            new SeekRequest(),
            b => b.OrderBy(d => d.Id));

        var page2 = await service.SeekAsync(
            docs.AsQueryable(),
            new SeekRequest { Token = page1.NextToken },
            b => b.OrderBy(d => d.Id));

        Assert.Equal(12, page1.Count);
        Assert.Equal(12, page2.Count);
        Assert.Equal(page1.Items[^1].Id.ToString(), new ObjectId($"{12:x24}").ToString());
        Assert.Equal("Doc 13", page2.Items[0].Name);
    }

    // ── Mixed sort directions ─────────────────────────────────────────────────

    [Fact]
    public async Task MixedDirections_PriceDescThenIdAsc_TraversesCorrectly()
    {
        var service = CreateService(defaultPageSize: 8);
        var docs    = SeedDocs(20);

        var seen = new List<decimal>();
        string? token = null;

        do
        {
            var page = await service.SeekAsync(
                docs.AsQueryable(),
                new SeekRequest { Token = token },
                b => b.OrderByDescending(d => d.Price).OrderBy(d => d.Id));

            seen.AddRange(page.Items.Select(d => d.Price));
            token = page.NextToken;
        } while (token != null);

        Assert.Equal(20, seen.Count);
        Assert.Equal(seen.OrderByDescending(p => p), seen);
    }

    // ── Filtered queryable ────────────────────────────────────────────────────

    [Fact]
    public async Task PreFilteredQueryable_PaginatesOnlyMatchingItems()
    {
        var service = CreateService(defaultPageSize: 50);
        var docs    = SeedDocs(30);

        var result = await service.SeekAsync(
            docs.AsQueryable().Where(d => d.Price > 30m),   // Doc 21..30
            new SeekRequest(),
            b => b.OrderBy(d => d.Id));

        Assert.Equal(10, result.Count);
        Assert.All(result.Items, d => Assert.True(d.Price > 30m));
        Assert.False(result.HasNext);
    }

    // ── Page size clamping ────────────────────────────────────────────────────

    [Fact]
    public async Task PageSize_ClampedToMaxPageSize()
    {
        var service = CreateService(defaultPageSize: 10, maxPageSize: 15);
        var docs    = SeedDocs(30);

        var result = await service.SeekAsync(
            docs.AsQueryable(),
            new SeekRequest { PageSize = 9999 },
            b => b.OrderBy(d => d.Id));

        Assert.Equal(15, result.Count);
        Assert.Equal(15, result.PageMetadata.PageSize);
    }

    // ── Empty source ──────────────────────────────────────────────────────────

    [Fact]
    public async Task EmptySource_ReturnsEmptyPage_NoTokens()
    {
        var service = CreateService();

        var result = await service.SeekAsync(
            Enumerable.Empty<Doc>().AsQueryable(),
            new SeekRequest(),
            b => b.OrderBy(d => d.Id));

        Assert.Empty(result.Items);
        Assert.Null(result.NextToken);
        Assert.Null(result.PreviousToken);
        Assert.False(result.HasNext);
    }
}
