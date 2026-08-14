namespace SeekKit.EntityFramework.Tests.Integration;

/// <summary>
/// Integration tests for paginating an <see cref="IQueryable{T}"/> of a scalar
/// type (e.g. <c>IQueryable&lt;int&gt;</c>), ordered by the element itself
/// (<c>x =&gt; x</c>) rather than by a property. Common when pre-filtering to a
/// set of ids before joining, as in <c>ISeekBuilder{T}.Select</c>.
/// </summary>
public sealed class ScalarKeySeekTests : IClassFixture<SeekFixture>, IDisposable
{
    private readonly ISeekFactory  _factory;
    private readonly TestDbContext _db;

    public ScalarKeySeekTests(SeekFixture fixture)
    {
        _factory = fixture.Factory;
        _db      = new TestDbContext();
        _db.Database.EnsureCreated();
        _db.Seed(count: 30); // products with Id 1-30
    }

    [Fact]
    public async Task OrderBy_IdentitySelector_ReturnsOrderedScalarPage()
    {
        var ids = _db.Products.Select(p => p.Id);

        var result = await _factory
            .CreateBuilder(ids)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(x => x)
            .ToSeekResultAsync();

        Assert.Equal(10, result.Items.Count);
        Assert.Equal(1,  result.Items[0]);
        Assert.Equal(10, result.Items[^1]);
        Assert.True(result.HasNext);
    }

    [Fact]
    public async Task OrderByDescending_IdentitySelector_NextToken_ContinuesToCorrectBatch()
    {
        var ids = _db.Products.Select(p => p.Id);

        var page1 = await _factory
            .CreateBuilder(ids)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderByDescending(x => x)
            .ToSeekResultAsync();

        var page2 = await _factory
            .CreateBuilder(ids)
            .WithRequest(new SeekRequest { Token = page1.NextToken, PageSize = 10 })
            .OrderByDescending(x => x)
            .ToSeekResultAsync();

        Assert.Equal(20, page2.Items[0]);
        Assert.True(page2.HasPrevious);
    }

    public void Dispose() => _db.Dispose();
}
