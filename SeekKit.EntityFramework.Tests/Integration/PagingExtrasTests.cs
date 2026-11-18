namespace SeekKit.EntityFramework.Tests.Integration;

/// <summary>
/// End-to-end coverage (real SQL against SQLite) for walking every page
/// (<c>ToPagesAsync</c> / <c>ToItemsAsync</c>), the opt-in total count, computed sort
/// expressions, and client-driven sorting through a <see cref="SeekSortMap{T}"/>.
/// </summary>
public sealed class PagingExtrasTests : IClassFixture<SeekFixture>, IDisposable
{
    private static readonly SeekSortMap<Product> Sorts = new SeekSortMap<Product>()
        .Add("createdAt", p => p.CreatedAt)
        .Add("name", p => p.Name)
        .Add("category", p => p.CategoryId)
        .Add("id", p => p.Id)
        .Default("createdAt:desc")
        .TieBreaker("id");

    private readonly ISeekFactory _factory;
    private readonly ISeekService _service;
    private readonly CommandCountInterceptor _commands = new();
    private readonly TestDbContext _db;

    public PagingExtrasTests(SeekFixture fixture)
    {
        _factory = fixture.Factory;
        _service = fixture.Service;
        _db      = new TestDbContext(_commands);
        _db.Database.EnsureCreated();
        _db.Seed(count: 30);
        _commands.Reset();
    }

    // ── Walking every page ────────────────────────────────────────────────────

    [Fact]
    public async Task ToPagesAsync_YieldsEveryPage_OneQueryEach()
    {
        var pages = new List<SeekResult<Product>>();

        await foreach (var page in _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 7 })
            .OrderBy(p => p.Id)
            .ToPagesAsync())
        {
            pages.Add(page);
        }

        Assert.Equal([7, 7, 7, 7, 2], pages.Select(p => p.Count));
        Assert.Equal(Enumerable.Range(1, 30), pages.SelectMany(p => p.Items).Select(p => p.Id));
        Assert.False(pages[^1].HasNext);
        Assert.Equal(5, _commands.Count);
    }

    [Fact]
    public async Task ToPagesAsync_IsLazy_NothingRunsUntilEnumerated_AndStopsWhenAbandoned()
    {
        var pages = _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 7 })
            .OrderBy(p => p.Id)
            .ToPagesAsync();

        Assert.Equal(0, _commands.Count);

        await foreach (var page in pages)
        {
            if (page.Items[0].Id > 7) break;   // stop on the second page
        }

        Assert.Equal(2, _commands.Count);
    }

    [Fact]
    public async Task ToItemsAsync_YieldsEveryRowInOrder()
    {
        var ids = new List<int>();

        await foreach (var product in _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 4 })
            .OrderByDescending(p => p.CreatedAt)
            .OrderBy(p => p.Id)
            .ToItemsAsync())
        {
            ids.Add(product.Id);
        }

        Assert.Equal(Enumerable.Range(1, 30).Reverse(), ids);
    }

    [Fact]
    public async Task ToItemsAsync_StartsFromTheRequestsToken()
    {
        var first = await _factory.CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        var ids = new List<int>();
        await foreach (var product in _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = first.NextToken, PageSize = 6 })
            .OrderBy(p => p.Id)
            .ToItemsAsync())
        {
            ids.Add(product.Id);
        }

        Assert.Equal(Enumerable.Range(11, 20), ids);
    }

    [Fact]
    public async Task ToItemsAsync_ThroughAProjection()
    {
        var names = new List<string>();

        await foreach (var item in _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 9 })
            .OrderBy(p => p.Id)
            .Select(q => q.Select(p => new { p.Id, Label = p.Name + " / " + p.Category!.Name }))
            .ToItemsAsync())
        {
            names.Add(item.Label);
        }

        Assert.Equal(30, names.Count);
        Assert.StartsWith("Product 01 / ", names[0]);
        Assert.StartsWith("Product 30 / ", names[^1]);
    }

    // ── Total count ───────────────────────────────────────────────────────────

    [Fact]
    public async Task TotalCount_IsNull_UnlessAskedFor()
    {
        var page = await _factory.CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Null(page.TotalCount);
        Assert.Equal(1, _commands.Count);
    }

    [Fact]
    public async Task WithTotalCount_CountsTheWholeFilteredQuery_OnEveryPage()
    {
        var electronics = _db.Products.Where(p => p.Category!.Name == "Electronics");   // 10 of 30

        var first = await _factory.CreateBuilder(electronics)
            .WithRequest(new SeekRequest { PageSize = 4 })
            .OrderBy(p => p.Id)
            .WithTotalCount()
            .ToSeekResultAsync();

        var second = await _factory.CreateBuilder(electronics)
            .WithRequest(new SeekRequest { Token = first.NextToken, PageSize = 4 })
            .OrderBy(p => p.Id)
            .WithTotalCount()
            .ToSeekResultAsync();

        Assert.Equal(10, first.TotalCount);
        Assert.Equal(4, first.Count);
        Assert.Equal(10, second.TotalCount);   // the keyset filter must not leak into the count
        Assert.Equal(4, _commands.Count);      // 2 × (count + page)
    }

    [Fact]
    public async Task WithTotalCount_False_TurnsItBackOff()
    {
        var page = await _factory.CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderBy(p => p.Id)
            .WithTotalCount()
            .WithTotalCount(include: false)
            .ToSeekResultAsync();

        Assert.Null(page.TotalCount);
    }

    [Fact]
    public async Task WithTotalCount_CarriesThroughSelectAndMap()
    {
        var page = await _factory.CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderBy(p => p.Id)
            .WithTotalCount()
            .Select(q => q.Select(p => new { p.Id, p.Name }))
            .ToSeekResultAsync();

        Assert.Equal(30, page.TotalCount);
        Assert.Equal(30, page.Map(x => x.Name).TotalCount);
    }

    [Fact]
    public async Task WithTotalCount_WhileWalkingPages_CountsOnce()
    {
        var totals = new List<long?>();

        await foreach (var page in _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(p => p.Id)
            .WithTotalCount()
            .ToPagesAsync())
        {
            totals.Add(page.TotalCount);
        }

        Assert.Equal([30, 30, 30], totals);
        Assert.Equal(4, _commands.Count);   // 1 count + 3 pages
    }

    [Fact]
    public void TotalCount_IsLeftOutOfJson_WhenNotRequested()
    {
        var result = new SeekResult<int> { Items = [1], Count = 1, PageMetadata = new PageMetadata { PageSize = 1 } };

        Assert.DoesNotContain("TotalCount", System.Text.Json.JsonSerializer.Serialize(result));

        result.TotalCount = 42;
        Assert.Contains("\"TotalCount\":42", System.Text.Json.JsonSerializer.Serialize(result));
    }

    // ── Computed sort expressions ─────────────────────────────────────────────

    [Fact]
    public async Task ComputedSortExpression_PagesInDatabaseOrder()
    {
        var expected = await _db.Products.OrderBy(p => p.Id % 7).ThenByDescending(p => p.Id).Select(p => p.Id).ToListAsync();

        var seen = new List<int>();
        await foreach (var product in _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 6 })
            .OrderBy(p => p.Id % 7)
            .OrderByDescending(p => p.Id)
            .ToItemsAsync())
        {
            seen.Add(product.Id);
        }

        Assert.Equal(expected, seen);
    }

    [Fact]
    public async Task ComputedStringExpression_PagesInDatabaseOrder_AndBack()
    {
        var expected = await _db.Products.OrderByDescending(p => p.Name.ToUpper()).ThenBy(p => p.Id).Select(p => p.Id).ToListAsync();

        Task<SeekResult<Product>> Page(string? token) => _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = token, PageSize = 8 })
            .OrderByDescending(p => p.Name.ToUpper())
            .OrderBy(p => p.Id)
            .ToSeekResultAsync()
            .AsTask();

        var first  = await Page(null);
        var second = await Page(first.NextToken);
        var back   = await Page(second.PreviousToken);

        Assert.Equal(expected.Take(8), first.Items.Select(p => p.Id));
        Assert.Equal(expected.Skip(8).Take(8), second.Items.Select(p => p.Id));
        Assert.Equal(expected.Take(8), back.Items.Select(p => p.Id));
    }

    [Fact]
    public void ComputedSortExpression_WithSelect_NeedsANamedResultProperty()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _factory
            .CreateBuilder(_db.Products)
            .OrderBy(p => p.Id % 7)
            .OrderBy(p => p.Id)
            .Select(q => q.Select(p => new { p.Id, p.Name })));

        Assert.Contains("resultPropertyName", ex.Message);
    }

    [Fact]
    public async Task ComputedSortExpression_WithSelect_WorksOnceNamed()
    {
        var expected = await _db.Products.OrderBy(p => p.Id % 7).ThenBy(p => p.Id).Select(p => p.Id).ToListAsync();

        var seen = new List<int>();
        await foreach (var item in _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 6 })
            .OrderBy(p => p.Id % 7, resultPropertyName: "Bucket")
            .OrderBy(p => p.Id)
            .Select(q => q.Select(p => new { Bucket = p.Id % 7, p.Id }))
            .ToItemsAsync())
        {
            seen.Add(item.Id);
        }

        Assert.Equal(expected, seen);
    }

    // ── Client-driven sorting ─────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("name")]
    [InlineData("name:desc")]
    [InlineData("category:asc,createdAt:desc")]
    [InlineData("category:desc,name:desc")]
    public async Task ApplySort_PagesInTheRequestedOrder(string? sort)
    {
        var expected = await Ordered(_db.Products, sort).Select(p => p.Id).ToListAsync();

        var seen = new List<int>();
        string? token = null;
        do
        {
            var page = await _service.SeekAsync(
                _db.Products,
                new SeekRequest { Token = token, PageSize = 7 },
                b => b.ApplySort(Sorts, sort));

            seen.AddRange(page.Items.Select(p => p.Id));
            token = page.NextToken;
        } while (token is not null);

        Assert.Equal(expected, seen);
    }

    [Fact]
    public async Task ApplySort_UnknownField_ThrowsBeforeAnyQuery()
    {
        await Assert.ThrowsAsync<SeekSortException>(async () => await _service.SeekAsync(
            _db.Products, new SeekRequest { PageSize = 7 }, b => b.ApplySort(Sorts, "price")));

        Assert.Equal(0, _commands.Count);
    }

    [Fact]
    public async Task ApplySort_ChangingTheSortMidWalk_RejectsTheOldToken()
    {
        var first = await _service.SeekAsync(
            _db.Products, new SeekRequest { PageSize = 7 }, b => b.ApplySort(Sorts, "name"));

        var ex = await Assert.ThrowsAsync<SeekTokenException>(async () => await _service.SeekAsync(
            _db.Products, new SeekRequest { Token = first.NextToken, PageSize = 7 }, b => b.ApplySort(Sorts, "name:desc")));

        Assert.Equal(SeekTokenError.SortMismatch, ex.Error);
    }

    // The same sort, written by hand, for the database to act as the oracle.
    private static IQueryable<Product> Ordered(IQueryable<Product> query, string? sort) => sort switch
    {
        null                          => query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id),
        "name"                        => query.OrderBy(p => p.Name).ThenBy(p => p.Id),
        "name:desc"                   => query.OrderByDescending(p => p.Name).ThenByDescending(p => p.Id),
        "category:asc,createdAt:desc" => query.OrderBy(p => p.CategoryId).ThenByDescending(p => p.CreatedAt).ThenBy(p => p.Id),
        "category:desc,name:desc"     => query.OrderByDescending(p => p.CategoryId).ThenByDescending(p => p.Name).ThenByDescending(p => p.Id),
        _ => throw new ArgumentOutOfRangeException(nameof(sort)),
    };

    public void Dispose() => _db.Dispose();
}
