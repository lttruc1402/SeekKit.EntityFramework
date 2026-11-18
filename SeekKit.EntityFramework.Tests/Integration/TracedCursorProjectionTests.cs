namespace SeekKit.EntityFramework.Tests.Integration;

/// <summary>
/// End-to-end coverage (real SQL against SQLite) for <c>Select()</c> projections where the
/// cursor value can't be read off the result by the sort column's own name and has to be
/// traced through the transformer instead — without the caller passing
/// <c>resultPropertyName</c>.
/// </summary>
public sealed class TracedCursorProjectionTests : IClassFixture<SeekFixture>, IDisposable
{
    private readonly ISeekFactory  _factory;
    private readonly TestDbContext _db;

    private sealed class ProductListItem
    {
        public int     ProductId       { get; set; }
        public string  Name            { get; set; } = "";
        public string  CategoryName    { get; set; } = "";
        public string? AltCategoryName { get; set; }
    }

    public TracedCursorProjectionTests(SeekFixture fixture)
    {
        _factory = fixture.Factory;
        _db      = new TestDbContext();
        _db.Database.EnsureCreated();
        _db.Seed(count: 30);
    }

    [Fact]
    public async Task IdentityOrderBy_ThroughJoinChainAndLeftJoin_PagesWithoutResultPropertyName()
    {
        // The shape that used to force resultPropertyName: pre-filtered ids, then a chain
        // of joins that re-wraps the entity several times before the final DTO projection.
        var ids = _db.Products.Select(p => p.Id);

        Task<SeekResult<ProductListItem>> Page(string? token) => _factory
            .CreateBuilder(ids)
            .WithRequest(new SeekRequest { Token = token, PageSize = 7 })
            .OrderByDescending(x => x)
            .Select(q => q
                .Join(_db.Products, id => id, p => p.Id, (id, p) => p)
                .Join(_db.Categories, p => p.CategoryId, c => c.Id, (p, c) => new { p, c })
                .LeftJoin(_db.Categories, x => x.p.CategoryId, alt => alt.Id, (x, alt) => new { x.p, x.c, alt })
                .Select(x => new ProductListItem
                {
                    ProductId       = x.p.Id,
                    Name            = x.p.Name,
                    CategoryName    = x.c.Name,
                    AltCategoryName = x.alt!.Name,
                }))
            .ToSeekResultAsync()
            .AsTask();

        var all = new List<ProductListItem>();
        string? token = null;
        int pages = 0;
        do
        {
            var page = await Page(token);
            all.AddRange(page.Items);
            token = page.NextToken;
            Assert.True(++pages <= 10, "runaway pagination");
        } while (token != null);

        Assert.Equal(5, pages);
        Assert.Equal(Enumerable.Range(1, 30), all.Select(x => x.ProductId).OrderBy(id => id));
        Assert.All(all, x => Assert.False(string.IsNullOrEmpty(x.CategoryName)));
    }

    [Fact]
    public async Task IdentityOrderBy_ThroughJoinChain_PreviousTokenReturnsToFirstPage()
    {
        var ids = _db.Products.Select(p => p.Id);

        Task<SeekResult<ProductListItem>> Page(string? token) => _factory
            .CreateBuilder(ids)
            .WithRequest(new SeekRequest { Token = token, PageSize = 10 })
            .OrderByDescending(x => x)
            .Select(q => q
                .Join(_db.Products, id => id, p => p.Id, (id, p) => p)
                .Join(_db.Categories, p => p.CategoryId, c => c.Id, (p, c) => new { p, c })
                .Select(x => new ProductListItem { ProductId = x.p.Id, Name = x.p.Name, CategoryName = x.c.Name }))
            .ToSeekResultAsync()
            .AsTask();

        var first  = await Page(null);
        var second = await Page(first.NextToken);
        var back   = await Page(second.PreviousToken);

        Assert.Equal(
            first.Items.Select(x => x.ProductId).OrderBy(id => id),
            back.Items.Select(x => x.ProductId).OrderBy(id => id));
        Assert.DoesNotContain(second.Items, x => first.Items.Any(f => f.ProductId == x.ProductId));
    }

    [Fact]
    public async Task MemberOrderBy_RenamedByProjection_PagesWithoutResultPropertyName()
    {
        // Sorted by CreatedAt/Id, but the projection exposes them as Created/Key — there is
        // no same-named property to read the cursor from.
        var seen = new List<int>();
        string? token = null;
        int pages = 0;
        do
        {
            var page = await _factory
                .CreateBuilder(_db.Products)
                .WithRequest(new SeekRequest { Token = token, PageSize = 8 })
                .OrderByDescending(p => p.CreatedAt)
                .OrderBy(p => p.Id)
                .Select(q => q.Select(p => new { Key = p.Id, Created = p.CreatedAt, p.Name }))
                .ToSeekResultAsync();

            seen.AddRange(page.Items.Select(x => x.Key));
            token = page.NextToken;
            Assert.True(++pages <= 10, "runaway pagination");
        } while (token != null);

        Assert.Equal(Enumerable.Range(1, 30), seen.OrderBy(id => id));
        Assert.Equal(30, seen.Distinct().Count());
    }

    [Fact]
    public void MemberOrderBy_DroppedByProjection_StillNamesTheMissingProperty()
    {
        // Neither readable by name nor traceable: the original, specific error must survive.
        var ex = Assert.Throws<InvalidOperationException>(() => _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 8 })
            .OrderBy(p => p.CreatedAt)
            .Select(q => q.Select(p => new { p.Id, p.Name })));

        Assert.Contains("CreatedAt", ex.Message);
    }

    public void Dispose() => _db.Dispose();
}
