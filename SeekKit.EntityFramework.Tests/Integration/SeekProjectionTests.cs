namespace SeekKit.EntityFramework.Tests.Integration;

/// <summary>
/// Integration tests for <see cref="ISeekBuilder{T}.Select{TResult}"/> — projected
/// (push-down join) keyset pagination.
/// </summary>
public sealed class SeekProjectionTests : IClassFixture<SeekFixture>, IDisposable
{
    private readonly ISeekFactory  _factory;
    private readonly TestDbContext _db;

    private static Func<IQueryable<Product>, IQueryable<ProductSummaryDto>> Transformer =>
        q => q.Select(p => new ProductSummaryDto
        {
            Id           = p.Id,
            CreatedAt    = p.CreatedAt,
            CategoryName = p.Category!.Name
        });

    public SeekProjectionTests(SeekFixture fixture)
    {
        _factory = fixture.Factory;
        _db      = new TestDbContext();
        _db.Database.EnsureCreated();
        _db.Seed(count: 30);
    }

    [Fact]
    public async Task Select_FirstPage_MatchesNonProjectedOrderAndIncludesJoinedData()
    {
        var expected = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        var projected = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(p => p.Id)
            .Select(Transformer)
            .ToSeekResultAsync();

        Assert.Equal(expected.Items.Select(p => p.Id), projected.Items.Select(p => p.Id));
        Assert.Equal(expected.Count, projected.Count);
        Assert.Equal(expected.HasNext, projected.HasNext);
        Assert.NotNull(projected.NextToken);
        Assert.All(projected.Items, p => Assert.False(string.IsNullOrEmpty(p.CategoryName)));
    }

    [Fact]
    public async Task Select_NextToken_ContinuesToCorrectBatch()
    {
        var page1 = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(p => p.Id)
            .Select(Transformer)
            .ToSeekResultAsync();

        var page2 = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = page1.NextToken, PageSize = 10 })
            .OrderBy(p => p.Id)
            .Select(Transformer)
            .ToSeekResultAsync();

        Assert.Equal(11, page2.Items[0].Id);
        Assert.Equal(20, page2.Items[^1].Id);
        Assert.True(page2.HasPrevious);
    }

    [Fact]
    public async Task Select_PreviousToken_ReturnsExactSameBatchAsOriginal()
    {
        var page1 = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(p => p.Id)
            .Select(Transformer)
            .ToSeekResultAsync();

        var page2 = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = page1.NextToken, PageSize = 10 })
            .OrderBy(p => p.Id)
            .Select(Transformer)
            .ToSeekResultAsync();

        var backToPage1 = await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = page2.PreviousToken, PageSize = 10 })
            .OrderBy(p => p.Id)
            .Select(Transformer)
            .ToSeekResultAsync();

        Assert.Equal(page1.Items.Select(p => p.Id), backToPage1.Items.Select(p => p.Id));
    }

    [Fact]
    public void Select_TransformerNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            _factory
                .CreateBuilder(_db.Products)
                .WithRequest(new SeekRequest())
                .OrderBy(p => p.Id)
                .Select<ProductSummaryDto>(null!);
        });
    }

    [Fact]
    public void Select_NoOrderByCalled_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            _factory
                .CreateBuilder(_db.Products)
                .WithRequest(new SeekRequest())
                // intentionally no OrderBy call
                .Select(Transformer);
        });
    }

    [Fact]
    public void Select_ResultMissingSortProperty_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            _factory
                .CreateBuilder(_db.Products)
                .WithRequest(new SeekRequest())
                .OrderBy(p => p.Id)
                .Select(q => q.Select(p => new { CategoryName = p.Category!.Name })); // no Id property
        });

        Assert.Contains("Id", ex.Message);
    }

    [Fact]
    public async Task Select_MultiColumnDescendingSort_ForwardTraversalCoversAllItemsInOrder()
    {
        var allIds = new List<int>();
        string? token = null;

        do
        {
            var page = await _factory
                .CreateBuilder(_db.Products)
                .WithRequest(new SeekRequest { Token = token, PageSize = 7 })
                .OrderByDescending(p => p.CreatedAt)
                .OrderBy(p => p.Id)
                .Select(Transformer)
                .ToSeekResultAsync();

            allIds.AddRange(page.Items.Select(p => p.Id));
            token = page.NextToken;
        }
        while (token is not null);

        // CreatedAt increases strictly with Id, so ordering by CreatedAt descending
        // (with Id ascending as tiebreaker) yields Id 30 down to Id 1.
        Assert.Equal(Enumerable.Range(1, 30).Reverse(), allIds);
        Assert.Equal(30, allIds.Distinct().Count());
    }

    [Fact]
    public async Task Select_ExecutesExactlyOneDatabaseRoundTrip()
    {
        var interceptor = new CommandCountInterceptor();
        using var db = new TestDbContext(interceptor);
        db.Database.EnsureCreated();
        db.Seed(count: 30);
        interceptor.Reset(); // ignore setup/seed commands — only count the paginated query

        var result = await _factory
            .CreateBuilder(db.Products)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(p => p.Id)
            .Select(Transformer)
            .ToSeekResultAsync();

        Assert.Equal(10, result.Count);
        Assert.Equal(1, interceptor.Count);
    }

    public void Dispose() => _db.Dispose();
}
