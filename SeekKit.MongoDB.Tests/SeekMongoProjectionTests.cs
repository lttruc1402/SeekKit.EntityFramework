using MongoDB.Driver;

namespace SeekKit.MongoDB.Tests;

/// <summary>
/// Tests for <see cref="ISeekMongoQueryableBuilder{T}.Select{TResult}"/> — projected
/// (push-down) keyset pagination over an <see cref="IQueryable{T}"/>. Uses an in-memory
/// queryable (no live MongoDB needed), exactly like <see cref="SeekMongoServiceTests"/>.
/// </summary>
public sealed class SeekMongoProjectionTests
{
    private sealed class Doc
    {
        public ObjectId  Id        { get; set; }
        public string    Name      { get; set; } = "";
        public DateTime  CreatedAt { get; set; }
    }

    private sealed class DocSummaryDto
    {
        public ObjectId Id          { get; set; }
        public DateTime CreatedAt   { get; set; }
        public string   DisplayName { get; set; } = "";
    }

    private static Func<IQueryable<Doc>, IQueryable<DocSummaryDto>> Transformer =>
        q => q.Select(d => new DocSummaryDto
        {
            Id          = d.Id,
            CreatedAt   = d.CreatedAt,
            DisplayName = d.Name.ToUpperInvariant()
        });

    private static ISeekMongoService CreateService(int defaultPageSize = 10)
    {
        var services = new ServiceCollection();
        services.AddSeekKitMongo(o => o.DefaultPageSize = defaultPageSize);
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
                CreatedAt = baseTime.AddMinutes(i),
            });
        }
        return docs;
    }

    [Fact]
    public async Task Select_FirstPage_MatchesNonProjectedOrderAndAppliesTransform()
    {
        var service = CreateService();
        var docs = SeedDocs(30);

        var expected = await service
            .CreateBuilder(docs.AsQueryable())
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(d => d.Id)
            .ToSeekResultAsync();

        var projected = await service
            .CreateBuilder(docs.AsQueryable())
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(d => d.Id)
            .Select(Transformer)
            .ToSeekResultAsync();

        Assert.Equal(expected.Items.Select(d => d.Id), projected.Items.Select(d => d.Id));
        Assert.Equal(expected.Count, projected.Count);
        Assert.Equal("DOC 1", projected.Items[0].DisplayName);
    }

    [Fact]
    public async Task Select_NextToken_ContinuesToCorrectBatch()
    {
        var service = CreateService();
        var docs = SeedDocs(30);

        var page1 = await service
            .CreateBuilder(docs.AsQueryable())
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(d => d.Id)
            .Select(Transformer)
            .ToSeekResultAsync();

        var page2 = await service
            .CreateBuilder(docs.AsQueryable())
            .WithRequest(new SeekRequest { Token = page1.NextToken, PageSize = 10 })
            .OrderBy(d => d.Id)
            .Select(Transformer)
            .ToSeekResultAsync();

        Assert.Equal("DOC 11", page2.Items[0].DisplayName);
        Assert.True(page2.HasPrevious);
    }

    [Fact]
    public async Task Select_PreviousToken_ReturnsExactSameBatchAsOriginal()
    {
        var service = CreateService();
        var docs = SeedDocs(30);

        var page1 = await service
            .CreateBuilder(docs.AsQueryable())
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderBy(d => d.Id)
            .Select(Transformer)
            .ToSeekResultAsync();

        var page2 = await service
            .CreateBuilder(docs.AsQueryable())
            .WithRequest(new SeekRequest { Token = page1.NextToken, PageSize = 10 })
            .OrderBy(d => d.Id)
            .Select(Transformer)
            .ToSeekResultAsync();

        var backToPage1 = await service
            .CreateBuilder(docs.AsQueryable())
            .WithRequest(new SeekRequest { Token = page2.PreviousToken, PageSize = 10 })
            .OrderBy(d => d.Id)
            .Select(Transformer)
            .ToSeekResultAsync();

        Assert.Equal(page1.Items.Select(d => d.Id), backToPage1.Items.Select(d => d.Id));
    }

    [Fact]
    public void Select_TransformerNull_ThrowsArgumentNullException()
    {
        var service = CreateService();
        var docs = SeedDocs(5);

        Assert.Throws<ArgumentNullException>(() =>
        {
            service
                .CreateBuilder(docs.AsQueryable())
                .WithRequest(new SeekRequest())
                .OrderBy(d => d.Id)
                .Select<DocSummaryDto>(null!);
        });
    }

    [Fact]
    public void Select_NoOrderByCalled_ThrowsInvalidOperationException()
    {
        var service = CreateService();
        var docs = SeedDocs(5);

        Assert.Throws<InvalidOperationException>(() =>
        {
            service
                .CreateBuilder(docs.AsQueryable())
                .WithRequest(new SeekRequest())
                // intentionally no OrderBy call
                .Select(Transformer);
        });
    }

    [Fact]
    public void Select_ResultMissingSortProperty_ThrowsInvalidOperationException()
    {
        var service = CreateService();
        var docs = SeedDocs(5);

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            service
                .CreateBuilder(docs.AsQueryable())
                .WithRequest(new SeekRequest())
                .OrderBy(d => d.Id)
                .Select(q => q.Select(d => new { DisplayName = d.Name })); // no Id property
        });

        Assert.Contains("Id", ex.Message);
    }

    // ── Aggregate origin (CI-safe: construction/chaining/error paths only — no
    //    live MongoDB is available in this environment) ─────────────────────────

    // Creating an aggregate fluent does not connect — safe with a dummy client.
    private static IAggregateFluent<Doc> Pipeline()
        => new MongoClient("mongodb://localhost:27017")
            .GetDatabase("test")
            .GetCollection<Doc>("docs")
            .Aggregate();

    private static Func<IAggregateFluent<Doc>, IAggregateFluent<DocSummaryDto>> AggregateTransformer =>
        p => p.Project(d => new DocSummaryDto
        {
            Id          = d.Id,
            CreatedAt   = d.CreatedAt,
            DisplayName = d.Name
        });

    [Fact]
    public void AggregateSelect_IsChainableAfterOrderByAndWithRequest()
    {
        var builder = CreateService()
            .CreateBuilder(Pipeline())
            .WithRequest(new SeekRequest())
            .OrderBy(d => d.Id)
            .Select(AggregateTransformer);

        Assert.NotNull(builder);
    }

    [Fact]
    public void AggregateSelect_TransformerNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            CreateService()
                .CreateBuilder(Pipeline())
                .WithRequest(new SeekRequest())
                .OrderBy(d => d.Id)
                .Select<DocSummaryDto>(null!);
        });
    }

    [Fact]
    public void AggregateSelect_NoOrderByCalled_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            CreateService()
                .CreateBuilder(Pipeline())
                .WithRequest(new SeekRequest())
                // intentionally no OrderBy call
                .Select(AggregateTransformer);
        });
    }

    [Fact]
    public void AggregateSelect_ResultMissingSortProperty_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            CreateService()
                .CreateBuilder(Pipeline())
                .WithRequest(new SeekRequest())
                .OrderBy(d => d.Id)
                .Select(p => p.Project(d => new { DisplayName = d.Name })); // no Id property
        });

        Assert.Contains("Id", ex.Message);
    }

    // ── Find origin (CI-safe: construction/chaining/error paths only) ───────────

    // Creating a find fluent does not connect — safe with a dummy client.
    private static IFindFluent<Doc, Doc> Find()
        => new MongoClient("mongodb://localhost:27017")
            .GetDatabase("test")
            .GetCollection<Doc>("docs")
            .Find(FilterDefinition<Doc>.Empty);

    private static Func<IFindFluent<Doc, Doc>, IFindFluent<Doc, DocSummaryDto>> FindTransformer =>
        f => f.Project(Builders<Doc>.Projection.Expression(d => new DocSummaryDto
        {
            Id          = d.Id,
            CreatedAt   = d.CreatedAt,
            DisplayName = d.Name
        }));

    [Fact]
    public void FindSelect_IsChainableAfterOrderByAndWithRequest()
    {
        var builder = CreateService()
            .CreateBuilder(Find())
            .WithRequest(new SeekRequest())
            .OrderBy(d => d.Id)
            .Select(FindTransformer);

        Assert.NotNull(builder);
    }

    [Fact]
    public void FindSelect_TransformerNull_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            CreateService()
                .CreateBuilder(Find())
                .WithRequest(new SeekRequest())
                .OrderBy(d => d.Id)
                .Select<DocSummaryDto>(null!);
        });
    }

    [Fact]
    public void FindSelect_NoOrderByCalled_ThrowsInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            CreateService()
                .CreateBuilder(Find())
                .WithRequest(new SeekRequest())
                // intentionally no OrderBy call
                .Select(FindTransformer);
        });
    }

    [Fact]
    public void FindSelect_ResultMissingSortProperty_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            CreateService()
                .CreateBuilder(Find())
                .WithRequest(new SeekRequest())
                .OrderBy(d => d.Id)
                .Select(f => f.Project(Builders<Doc>.Projection.Expression(d => new { DisplayName = d.Name }))); // no Id property
        });

        Assert.Contains("Id", ex.Message);
    }
}
