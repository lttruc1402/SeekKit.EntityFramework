namespace SeekKit.EntityFramework.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="ISeekBuilder{T}"/> end-to-end pagination.
/// Uses an in-memory SQLite database (via <see cref="TestDbContext"/>) and a real
/// EF Core queryable — no mocks.
/// </summary>
public sealed class SeekBuilderTests : IClassFixture<SeekFixture>, IDisposable
{
    private readonly ISeekFactory  _factory;
    private readonly TestDbContext _db;

    public SeekBuilderTests(SeekFixture fixture)
    {
        _factory = fixture.Factory;
        _db      = new TestDbContext();
        _db.Database.EnsureCreated();
        _db.Seed(count: 30); // products with Id 1-30
    }

    // ── First page ────────────────────────────────────────────────────────────

    [Fact]
    public async Task FirstPage_DefaultPageSize_Returns10Items()
    {
        var result = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest())
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(10, result.Count);
        Assert.Equal(10, result.PageMetadata.PageSize);
    }

    [Fact]
    public async Task FirstPage_ItemsAreOrderedById_Ascending()
    {
        var result = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest())
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(1,  result.Items[0].Id);
        Assert.Equal(10, result.Items[^1].Id);
        Assert.Equal(result.Items.Select(p => p.Id), result.Items.Select(p => p.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task FirstPage_HasNextTrue_WhenMoreItemsExist()
    {
        var result = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.True(result.HasNext);
        Assert.NotNull(result.NextToken);
    }

    [Fact]
    public async Task FirstPage_HasPreviousFalse_Always()
    {
        var result = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest())
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.False(result.HasPrevious);
        Assert.Null(result.PreviousToken);
    }

    [Fact]
    public async Task FirstPage_ExplicitPageSize_ReturnsCorrectCount()
    {
        var result = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(5, result.Count);
        Assert.Equal(5, result.PageMetadata.PageSize);
    }

    // ── Empty dataset ─────────────────────────────────────────────────────────

    [Fact]
    public async Task EmptyQuery_ReturnsZeroItems_NoTokens()
    {
        var result = await _factory
            .CreateBuilder(_db.Products.Where(p => p.Id < 0))
            .WithRequest(new SeekRequest())
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(0,    result.Count);
        Assert.False(result.HasNext);
        Assert.False(result.HasPrevious);
        Assert.Null(result.NextToken);
        Assert.Null(result.PreviousToken);
    }

    // ── Page size boundary ────────────────────────────────────────────────────

    [Fact]
    public async Task PageSizeLargerThanTotal_ReturnsAllItems_NoNextToken()
    {
        var result = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 100 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(30, result.Count);
        Assert.False(result.HasNext);
        Assert.Null(result.NextToken);
    }

    [Fact]
    public async Task PageSize_ClampedToMinPageSize_WhenBelowMin()
    {
        // SeekFixture sets MinPageSize = 1
        var result = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 0 }) // below min → clamped to 1
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(1, result.PageMetadata.PageSize);
        Assert.Equal(1, result.Count);
    }

    [Fact]
    public async Task PageSize_ClampedToMaxPageSize_WhenAboveMax()
    {
        // SeekFixture sets MaxPageSize = 100; 30 items total so we get all
        var result = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 9999 }) // above max → clamped to 100
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(100, result.PageMetadata.PageSize);
        Assert.Equal(30,  result.Count); // only 30 products exist
    }

    // ── Forward navigation ────────────────────────────────────────────────────

    [Fact]
    public async Task NextPage_ReturnsCorrectBatch()
    {
        var page1 = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        var page2 = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = page1.NextToken, PageSize = 10 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(10, page2.Count);
        Assert.Equal(11, page2.Items[0].Id);
        Assert.Equal(20, page2.Items[^1].Id);
    }

    [Fact]
    public async Task NextPage_HasPreviousTrue()
    {
        var page1 = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        var page2 = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = page1.NextToken, PageSize = 10 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.True(page2.HasPrevious);
        Assert.NotNull(page2.PreviousToken);
    }

    [Fact]
    public async Task ThirdPage_HasBothTokens()
    {
        var page1 = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        var page2 = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = page1.NextToken, PageSize = 10 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        var page3 = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = page2.NextToken, PageSize = 10 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(10,  page3.Count);
        Assert.Equal(21,  page3.Items[0].Id);
        Assert.Equal(30,  page3.Items[^1].Id);
        Assert.False(page3.HasNext);           // last page
        Assert.True(page3.HasPrevious);
    }

    // ── Backward navigation ───────────────────────────────────────────────────

    [Fact]
    public async Task PreviousPage_ReturnsExactSameBatchAsOriginal()
    {
        var page1 = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        var page2 = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = page1.NextToken, PageSize = 10 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        // Navigate back
        var backToPage1 = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = page2.PreviousToken, PageSize = 10 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(10, backToPage1.Count);
        Assert.Equal(page1.Items.Select(p => p.Id), backToPage1.Items.Select(p => p.Id));
    }

    // ── Full traverse ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ForwardTraversal_CoversAllItems_NoGapsNoDuplicates()
    {
        var allIds = new List<int>();
        string? token = null;

        do
        {
            var page = await _factory
                .CreateBuilder(_db.Products)
                .WithRequest(new SeekRequest { Token = token, PageSize = 7 })
                .OrderBy(p => p.Id)
                .ToSeekResultAsync();

            allIds.AddRange(page.Items.Select(p => p.Id));
            token = page.NextToken;
        }
        while (token is not null);

        // 30 items, page size 7 → 5 pages (7+7+7+7+2)
        Assert.Equal(30, allIds.Count);
        Assert.Equal(Enumerable.Range(1, 30).ToList(), allIds);
    }

    // ── Descending order ──────────────────────────────────────────────────────

    [Fact]
    public async Task DescendingOrder_FirstPage_StartsFromHighestId()
    {
        var result = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderByDescending(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(5,  result.Count);
        Assert.Equal(30, result.Items[0].Id);
        Assert.Equal(26, result.Items[^1].Id);
    }

    [Fact]
    public async Task DescendingOrder_NextPage_ContinuesCorrectly()
    {
        var page1 = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderByDescending(p => p.Id)
            .ToSeekResultAsync();

        var page2 = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = page1.NextToken, PageSize = 10 })
            .OrderByDescending(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(20, page2.Items[0].Id);
        Assert.Equal(11, page2.Items[^1].Id);
    }

    // ── Multi-column sort ─────────────────────────────────────────────────────

    [Fact]
    public async Task MultiColumnSort_PriceDescThenNameAsc_FirstPageCorrect()
    {
        // Price desc: 300, 290, … → Id 30, 29, …
        var result = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderByDescending(p => p.Price)
            .OrderBy(p => p.Name)
            .ToSeekResultAsync();

        Assert.Equal(5,  result.Count);
        Assert.Equal(30, result.Items[0].Id);
    }

    [Fact]
    public async Task MultiColumnSort_ForwardTraversal_CoversAll()
    {
        var allIds = new List<int>();
        string? token = null;

        do
        {
            var page = await _factory
                .CreateBuilder(_db.Products)
                .WithRequest(new SeekRequest { Token = token, PageSize = 10 })
                .OrderByDescending(p => p.Price)
                .OrderBy(p => p.Id)
                .ToSeekResultAsync();

            allIds.AddRange(page.Items.Select(p => p.Id));
            token = page.NextToken;
        }
        while (token is not null);

        Assert.Equal(30, allIds.Count);
        Assert.Equal(30, allIds.Distinct().Count()); // no duplicates
    }

    // ── Map after pagination ──────────────────────────────────────────────────

    [Fact]
    public async Task Map_AfterPagination_PreservesTokensAndCount()
    {
        var result = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        var mapped = result.Map(p => p.Name);

        Assert.Equal(result.Count,         mapped.Count);
        Assert.Equal(result.NextToken,     mapped.NextToken);
        Assert.Equal(result.PreviousToken, mapped.PreviousToken);
        Assert.Equal("Product 01",         mapped.Items[0]);
        Assert.Equal("Product 05",         mapped.Items[^1]);
    }

    // ── No order fields guard ─────────────────────────────────────────────────

    [Fact]
    public async Task NoOrderFields_Throws_InvalidOperationException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _factory
                .CreateBuilder(_db.Products)
                .WithRequest(new SeekRequest())
                // intentionally no OrderBy call
                .ToSeekResultAsync();
        });
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose() => _db.Dispose();
}
