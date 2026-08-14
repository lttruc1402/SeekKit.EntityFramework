namespace SeekKit.EntityFramework.Tests.Integration;

/// <summary>
/// Reproduces the real-world usage pattern that surfaced the identity-selector bug:
/// pre-filter to an <c>IQueryable&lt;int&gt;</c> of ids, order by the element itself,
/// then push a join/projection down onto only the limited page via <c>Select</c>.
/// </summary>
public sealed class ScalarKeyProjectionTests : IClassFixture<SeekFixture>, IDisposable
{
    private readonly ISeekFactory  _factory;
    private readonly TestDbContext _db;

    private sealed class ProductSummary
    {
        public int    Id   { get; set; }
        public string Name { get; set; } = "";
    }

    public ScalarKeyProjectionTests(SeekFixture fixture)
    {
        _factory = fixture.Factory;
        _db      = new TestDbContext();
        _db.Database.EnsureCreated();
        _db.Seed(count: 30);
    }

    [Fact]
    public async Task Select_WithIdentityOrderBy_AutoDetectsJoinKey_ReturnsProjectedPage()
    {
        var ids = _db.Products.Select(p => p.Id);

        // No resultPropertyName — matches ids.Join(entities, x => x, e => e.Id,
        // (_, e) => new TResult { Id = e.Id, ... }), so the "Id" cursor property
        // should be auto-detected from the join's inner key selector.
        var result = await _factory
            .CreateBuilder(ids)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderByDescending(x => x)
            .Select(q => q.Join(_db.Products, id => id, p => p.Id,
                (_, p) => new ProductSummary { Id = p.Id, Name = p.Name }))
            .ToSeekResultAsync();

        Assert.Equal(10, result.Items.Count);
        Assert.Equal(30, result.Items[0].Id);
        Assert.True(result.HasNext);
    }

    [Fact]
    public async Task Select_WithIdentityOrderBy_AutoDetectsDirectSelectKey_ReturnsProjectedPage()
    {
        var ids = _db.Products.Select(p => p.Id);

        // No resultPropertyName — matches ids.Select(id => new TResult { Id = id }),
        // the identity parameter assigned directly to a member, no join involved.
        var result = await _factory
            .CreateBuilder(ids)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderByDescending(x => x)
            .Select(q => q.Select(id => new ProductSummary { Id = id, Name = "" }))
            .ToSeekResultAsync();

        Assert.Equal(10, result.Items.Count);
        Assert.Equal(30, result.Items[0].Id);
        Assert.True(result.HasNext);
    }

    [Fact]
    public async Task Select_WithIdentityOrderBy_AndExplicitResultPropertyName_ReturnsProjectedPage()
    {
        var ids = _db.Products.Select(p => p.Id);

        var result = await _factory
            .CreateBuilder(ids)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderByDescending(x => x, resultPropertyName: nameof(ProductSummary.Id))
            .Select(q => q.Join(_db.Products, id => id, p => p.Id,
                (_, p) => new ProductSummary { Id = p.Id, Name = p.Name }))
            .ToSeekResultAsync();

        Assert.Equal(10, result.Items.Count);
        Assert.Equal(30, result.Items[0].Id);
        Assert.True(result.HasNext);
    }

    [Fact]
    public async Task Select_WithIdentityOrderBy_AutoDetectsAliasedMemberName_ReturnsProjectedPage()
    {
        var ids = _db.Products.Select(p => p.Id);

        // The join key lands on a member named "id" — nothing like the source
        // property "Id"/"ProductId" — proving detection matches by structural
        // value equality, not by any naming convention.
        var result = await _factory
            .CreateBuilder(ids)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderByDescending(x => x)
            .Select(q => q.Join(_db.Products, id => id, p => p.Id,
                (_, p) => new { id = p.Id, name = p.Name }))
            .ToSeekResultAsync();

        Assert.Equal(10, result.Items.Count);
        Assert.Equal(30, result.Items[0].id);
        Assert.True(result.HasNext);
    }

    [Fact]
    public async Task Select_WithIdentityOrderBy_UndetectableTransform_ThrowsActionableError()
    {
        var ids = _db.Products.Select(p => p.Id);

        // No member is a direct copy of the join key (it's offset by 1000), so
        // auto-detection can't find a safe match and must not guess.
        var ex = Assert.Throws<InvalidOperationException>(() => _factory
            .CreateBuilder(ids)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderByDescending(x => x)
            .Select(q => q.Join(_db.Products, id => id, p => p.Id,
                (_, p) => new ProductSummary { Id = p.Id + 1000, Name = p.Name })));

        Assert.Contains("resultPropertyName", ex.Message);
    }

    public void Dispose() => _db.Dispose();
}
