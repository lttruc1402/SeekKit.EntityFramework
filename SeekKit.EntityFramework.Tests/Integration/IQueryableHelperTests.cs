using SeekKit.EntityFramework.Builders;
using SeekKit.EntityFramework.Helpers;

namespace SeekKit.EntityFramework.Tests.Integration;

/// <summary>
/// Integration tests for the <see cref="IQueryableHelper"/> extension methods —
/// <c>ToSeekBuilder</c>/<c>ToSeekResultAsync</c> entry points that resolve
/// <see cref="ISeekFactory"/>/<see cref="ISeekService"/> from an <see cref="IServiceProvider"/>,
/// or accept them directly.
/// </summary>
public sealed class IQueryableHelperTests : IClassFixture<SeekFixture>, IDisposable
{
    private readonly SeekFixture   _fixture;
    private readonly TestDbContext _db;

    private static Func<IQueryable<Product>, IQueryable<ProductSummaryDto>> Transformer =>
        q => q.Select(p => new ProductSummaryDto
        {
            Id           = p.Id,
            CreatedAt    = p.CreatedAt,
            CategoryName = p.Category!.Name
        });

    public IQueryableHelperTests(SeekFixture fixture)
    {
        _fixture = fixture;
        _db      = new TestDbContext();
        _db.Database.EnsureCreated();
        _db.Seed(count: 30);
    }

    // ── ToSeekBuilder(IServiceProvider) ──────────────────────────────────────

    [Fact]
    public async Task ToSeekBuilder_ServiceProvider_ReturnsWorkingBuilder()
    {
        var result = await _db.Products
            .ToSeekBuilder(_fixture.ServiceProvider)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(10, result.Count);
        Assert.Equal(1, result.Items[0].Id);
    }

    [Fact]
    public async Task ToSeekBuilder_ServiceProviderWithRequest_PrePopulatesRequest()
    {
        var result = await _db.Products
            .ToSeekBuilder(_fixture.ServiceProvider, new SeekRequest { PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void ToSeekBuilder_ServiceProviderNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _db.Products.ToSeekBuilder((IServiceProvider)null!));
    }

    // ── ToSeekBuilder(ISeekFactory) ───────────────────────────────────────────

    [Fact]
    public async Task ToSeekBuilder_SeekFactory_ReturnsWorkingBuilder()
    {
        var result = await _db.Products
            .ToSeekBuilder(_fixture.Factory)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(10, result.Count);
        Assert.Equal(1, result.Items[0].Id);
    }

    [Fact]
    public async Task ToSeekBuilder_SeekFactoryWithRequest_PrePopulatesRequest()
    {
        var result = await _db.Products
            .ToSeekBuilder(_fixture.Factory, new SeekRequest { PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void ToSeekBuilder_SeekFactoryNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _db.Products.ToSeekBuilder((ISeekFactory)null!));
    }

    // ── ToSeekBuilder(ISeekService) ───────────────────────────────────────────

    [Fact]
    public async Task ToSeekBuilder_SeekService_ReturnsWorkingBuilder()
    {
        var result = await _db.Products
            .ToSeekBuilder(_fixture.Service)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(10, result.Count);
        Assert.Equal(1, result.Items[0].Id);
    }

    [Fact]
    public async Task ToSeekBuilder_SeekServiceWithRequest_PrePopulatesRequest()
    {
        var result = await _db.Products
            .ToSeekBuilder(_fixture.Service, new SeekRequest { PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(5, result.Count);
    }

    [Fact]
    public void ToSeekBuilder_SeekServiceNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _db.Products.ToSeekBuilder((ISeekService)null!));
    }

    // ── ToSeekResultAsync<T, TResult>(IServiceProvider, ...) ──────────────────

    [Fact]
    public async Task ToSeekResultAsync_ServiceProviderWithTransformer_ReturnsProjectedPage()
    {
        var result = await _db.Products.ToSeekResultAsync(
            _fixture.ServiceProvider,
            new SeekRequest { PageSize = 10 },
            Transformer,
            configure: b => b.OrderBy(p => p.Id));

        Assert.Equal(10, result.Count);
        Assert.Equal(1, result.Items[0].Id);
        Assert.All(result.Items, p => Assert.False(string.IsNullOrEmpty(p.CategoryName)));
    }

    [Fact]
    public void ToSeekResultAsync_ServiceProviderWithTransformerNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _db.Products.ToSeekResultAsync(
                _fixture.ServiceProvider,
                new SeekRequest(),
                (Func<IQueryable<Product>, IQueryable<ProductSummaryDto>>)null!,
                configure: b => b.OrderBy(p => p.Id)));
    }

    // ── ToSeekResultAsync<T, TResult>(ISeekFactory, ...) ──────────────────────

    [Fact]
    public async Task ToSeekResultAsync_SeekFactoryWithTransformer_ReturnsProjectedPage()
    {
        var result = await _db.Products.ToSeekResultAsync(
            _fixture.Factory,
            new SeekRequest { PageSize = 10 },
            Transformer,
            configure: b => b.OrderBy(p => p.Id));

        Assert.Equal(10, result.Count);
        Assert.Equal(1, result.Items[0].Id);
        Assert.All(result.Items, p => Assert.False(string.IsNullOrEmpty(p.CategoryName)));
    }

    [Fact]
    public void ToSeekResultAsync_SeekFactoryWithTransformerNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _db.Products.ToSeekResultAsync(
                _fixture.Factory,
                new SeekRequest(),
                (Func<IQueryable<Product>, IQueryable<ProductSummaryDto>>)null!,
                configure: b => b.OrderBy(p => p.Id)));
    }

    // ── ToSeekResultAsync<T, TResult>(ISeekService, ...) ──────────────────────

    [Fact]
    public async Task ToSeekResultAsync_SeekServiceWithTransformer_ReturnsProjectedPage()
    {
        var result = await _db.Products.ToSeekResultAsync(
            _fixture.Service,
            new SeekRequest { PageSize = 10 },
            Transformer,
            configure: b => b.OrderBy(p => p.Id));

        Assert.Equal(10, result.Count);
        Assert.Equal(1, result.Items[0].Id);
        Assert.All(result.Items, p => Assert.False(string.IsNullOrEmpty(p.CategoryName)));
    }

    [Fact]
    public void ToSeekResultAsync_SeekServiceWithTransformerNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _db.Products.ToSeekResultAsync(
                _fixture.Service,
                new SeekRequest(),
                (Func<IQueryable<Product>, IQueryable<ProductSummaryDto>>)null!,
                configure: b => b.OrderBy(p => p.Id)));
    }

    public void Dispose() => _db.Dispose();
}
