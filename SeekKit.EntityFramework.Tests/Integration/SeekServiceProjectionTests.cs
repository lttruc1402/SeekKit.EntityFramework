namespace SeekKit.EntityFramework.Tests.Integration;

/// <summary>
/// Integration tests for the one-call <see cref="ISeekService.SeekAsync{T, TResult}"/>
/// overloads — projected pagination without building the fluent chain yourself.
/// </summary>
public sealed class SeekServiceProjectionTests : IClassFixture<SeekFixture>, IDisposable
{
    private readonly ISeekService  _service;
    private readonly TestDbContext _db;

    private static Func<IQueryable<Product>, IQueryable<ProductSummaryDto>> Transformer =>
        q => q.Select(p => new ProductSummaryDto
        {
            Id           = p.Id,
            CreatedAt    = p.CreatedAt,
            CategoryName = p.Category!.Name
        });

    public SeekServiceProjectionTests(SeekFixture fixture)
    {
        _service = fixture.Service;
        _db      = new TestDbContext();
        _db.Database.EnsureCreated();
        _db.Seed(count: 30);
    }

    [Fact]
    public async Task SeekAsync_WithTransformer_ReturnsProjectedPageMatchingNonProjectedOrder()
    {
        var expected = await _service.CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        var projected = await _service.SeekAsync(
            _db.Products,
            new SeekRequest { PageSize = 10 },
            Transformer,
            b => b.OrderBy(p => p.Id));

        Assert.Equal(expected.Items.Select(p => p.Id), projected.Items.Select(p => p.Id));
        Assert.Equal(expected.Count, projected.Count);
        Assert.Equal(expected.HasNext, projected.HasNext);
        Assert.All(projected.Items, p => Assert.False(string.IsNullOrEmpty(p.CategoryName)));
    }

    [Fact]
    public async Task SeekAsync_WithTransformer_NextToken_ContinuesToCorrectBatch()
    {
        var page1 = await _service.SeekAsync(
            _db.Products,
            new SeekRequest { PageSize = 10 },
            Transformer,
            b => b.OrderBy(p => p.Id));

        var page2 = await _service.SeekAsync(
            _db.Products,
            new SeekRequest { Token = page1.NextToken, PageSize = 10 },
            Transformer,
            b => b.OrderBy(p => p.Id));

        Assert.Equal(11, page2.Items[0].Id);
        Assert.True(page2.HasPrevious);
    }

    [Fact]
    public async Task SeekAsync_WithTransformerAndConfigureOption_AppliesOptionOverride()
    {
        var result = await _service.SeekAsync(
            _db.Products,
            new SeekRequest { PageSize = 9999 },   // above MaxPageSize override below
            Transformer,
            b => b.OrderBy(p => p.Id),
            configureOption: o => o.MaxPageSize = 5);

        Assert.Equal(5, result.Count);
        Assert.Equal(5, result.PageMetadata.PageSize);
    }

    [Fact]
    public async Task SeekAsync_WithTransformer_TransformerNull_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.SeekAsync(
                _db.Products,
                new SeekRequest(),
                (Func<IQueryable<Product>, IQueryable<ProductSummaryDto>>)null!,
                b => b.OrderBy(p => p.Id)).AsTask());
    }

    [Fact]
    public async Task SeekAsync_WithTransformer_ConfigureNull_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.SeekAsync(
                _db.Products,
                new SeekRequest(),
                Transformer,
                configure: null!).AsTask());
    }

    public void Dispose() => _db.Dispose();
}
