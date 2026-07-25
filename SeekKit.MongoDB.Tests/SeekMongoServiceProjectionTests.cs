using MongoDB.Driver;

namespace SeekKit.MongoDB.Tests;

/// <summary>
/// Tests for the one-call <see cref="ISeekMongoService.SeekAsync{T, TResult}"/>
/// overloads — projected pagination without building the fluent chain yourself.
/// </summary>
public sealed class SeekMongoServiceProjectionTests
{
    private sealed class Doc
    {
        public ObjectId Id        { get; set; }
        public string   Name      { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    private sealed class DocSummaryDto
    {
        public ObjectId Id          { get; set; }
        public DateTime CreatedAt   { get; set; }
        public string   DisplayName { get; set; } = "";
    }

    private static Func<IQueryable<Doc>, IQueryable<DocSummaryDto>> QueryableTransformer =>
        q => q.Select(d => new DocSummaryDto
        {
            Id          = d.Id,
            CreatedAt   = d.CreatedAt,
            DisplayName = d.Name.ToUpperInvariant()
        });

    private static Func<IAggregateFluent<Doc>, IAggregateFluent<DocSummaryDto>> AggregateTransformer =>
        p => p.Project(d => new DocSummaryDto
        {
            Id          = d.Id,
            CreatedAt   = d.CreatedAt,
            DisplayName = d.Name
        });

    private static Func<IFindFluent<Doc, Doc>, IFindFluent<Doc, DocSummaryDto>> FindTransformer =>
        f => f.Project(Builders<Doc>.Projection.Expression(d => new DocSummaryDto
        {
            Id          = d.Id,
            CreatedAt   = d.CreatedAt,
            DisplayName = d.Name
        }));

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

    // Creating an aggregate/find fluent does not connect — safe with a dummy client.
    private static IAggregateFluent<Doc> Pipeline()
        => new MongoClient("mongodb://localhost:27017")
            .GetDatabase("test")
            .GetCollection<Doc>("docs")
            .Aggregate();

    private static IFindFluent<Doc, Doc> Find()
        => new MongoClient("mongodb://localhost:27017")
            .GetDatabase("test")
            .GetCollection<Doc>("docs")
            .Find(FilterDefinition<Doc>.Empty);

    // ── IQueryable<T> — full correctness (in-memory, no live MongoDB needed) ────

    [Fact]
    public async Task SeekAsync_QueryableWithTransformer_ReturnsProjectedPage()
    {
        var service = CreateService();
        var docs = SeedDocs(30);

        var expected = await service.SeekAsync(
            docs.AsQueryable(), new SeekRequest { PageSize = 10 }, b => b.OrderBy(d => d.Id));

        var projected = await service.SeekAsync(
            docs.AsQueryable(), new SeekRequest { PageSize = 10 }, QueryableTransformer, b => b.OrderBy(d => d.Id));

        Assert.Equal(expected.Items.Select(d => d.Id), projected.Items.Select(d => d.Id));
        Assert.Equal("DOC 1", projected.Items[0].DisplayName);
    }

    [Fact]
    public async Task SeekAsync_QueryableWithTransformer_NextToken_ContinuesToCorrectBatch()
    {
        var service = CreateService();
        var docs = SeedDocs(30);

        var page1 = await service.SeekAsync(
            docs.AsQueryable(), new SeekRequest { PageSize = 10 }, QueryableTransformer, b => b.OrderBy(d => d.Id));

        var page2 = await service.SeekAsync(
            docs.AsQueryable(), new SeekRequest { Token = page1.NextToken, PageSize = 10 }, QueryableTransformer, b => b.OrderBy(d => d.Id));

        Assert.Equal("DOC 11", page2.Items[0].DisplayName);
        Assert.True(page2.HasPrevious);
    }

    [Fact]
    public async Task SeekAsync_QueryableWithTransformer_TransformerNull_ThrowsArgumentNullException()
    {
        var service = CreateService();
        var docs = SeedDocs(5);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.SeekAsync(
                docs.AsQueryable(), new SeekRequest(),
                (Func<IQueryable<Doc>, IQueryable<DocSummaryDto>>)null!,
                b => b.OrderBy(d => d.Id)).AsTask());
    }

    // ── IMongoCollection<T> — delegates straight to the IQueryable<T> overload,
    //    so only the null-guard is checked here (calling through would need a live
    //    MongoDB connection). ─────────────────────────────────────────────────────

    [Fact]
    public async Task SeekAsync_CollectionWithTransformer_CollectionNull_ThrowsArgumentNullException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.SeekAsync(
                (IMongoCollection<Doc>)null!, new SeekRequest(),
                QueryableTransformer, b => b.OrderBy(d => d.Id)).AsTask());
    }

    // ── IAggregateFluent<T> / IFindFluent<T, T> — CI-safe: construction/error paths
    //    only, no live MongoDB is available in this environment. ─────────────────

    [Fact]
    public async Task SeekAsync_AggregateWithTransformer_TransformerNull_ThrowsArgumentNullException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.SeekAsync(
                Pipeline(), new SeekRequest(),
                (Func<IAggregateFluent<Doc>, IAggregateFluent<DocSummaryDto>>)null!,
                b => b.OrderBy(d => d.Id)).AsTask());
    }

    [Fact]
    public async Task SeekAsync_AggregateWithTransformer_ConfigureNull_ThrowsArgumentNullException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.SeekAsync(
                Pipeline(), new SeekRequest(), AggregateTransformer, configure: null!).AsTask());
    }

    [Fact]
    public async Task SeekAsync_FindWithTransformer_TransformerNull_ThrowsArgumentNullException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.SeekAsync(
                Find(), new SeekRequest(),
                (Func<IFindFluent<Doc, Doc>, IFindFluent<Doc, DocSummaryDto>>)null!,
                b => b.OrderBy(d => d.Id)).AsTask());
    }

    [Fact]
    public async Task SeekAsync_FindWithTransformer_ConfigureNull_ThrowsArgumentNullException()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.SeekAsync(
                Find(), new SeekRequest(), FindTransformer, configure: null!).AsTask());
    }
}
