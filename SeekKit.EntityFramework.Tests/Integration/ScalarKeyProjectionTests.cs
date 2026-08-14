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
    public async Task Select_WithIdentityOrderBy_AndResultPropertyName_ReturnsProjectedPage()
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
    public async Task Select_WithIdentityOrderBy_AndNoResultPropertyName_ThrowsActionableError()
    {
        var ids = _db.Products.Select(p => p.Id);

        var ex = Assert.Throws<InvalidOperationException>(() => _factory
            .CreateBuilder(ids)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderByDescending(x => x)
            .Select(q => q.Join(_db.Products, id => id, p => p.Id,
                (_, p) => new ProductSummary { Id = p.Id, Name = p.Name })));

        Assert.Contains("resultPropertyName", ex.Message);
    }

    public void Dispose() => _db.Dispose();
}
