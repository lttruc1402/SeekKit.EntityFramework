using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using SeekKit.Core;
using SeekKit.Core.Models;
using SeekKit.MongoDB.Builders;
using Xunit;

namespace SeekKit.MongoDB.IntegrationTests;

/// <summary>
/// End-to-end tests against a real MongoDB for nullable and string sort keys, the total
/// count, walking every page, client-driven sorting, and computed sort keys — each across
/// the three entry points (LINQ queryable, aggregation pipeline, find). MongoDB's own
/// sort is the oracle: every walk is compared with the same sort run unpaged.
/// </summary>
public sealed class PagingExtrasTests : IntegrationTestBase
{
    public PagingExtrasTests(MongoFixture fixture) : base(fixture) { }

    public sealed class Note
    {
        public ObjectId Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Owner { get; set; }
        public int? Score { get; set; }
        public int Seq { get; set; }
    }

    private static readonly SeekSortMap<Note> Sorts = new SeekSortMap<Note>()
        .Add("title", n => n.Title)
        .Add("score", n => n.Score)
        .Add("seq", n => n.Seq)
        .Add("id", n => n.Id)
        .Default("seq")
        .TieBreaker("id");

    public static TheoryData<string> Origins => new() { "queryable", "aggregate", "find" };

    private async Task<IMongoCollection<Note>> SeedNotesAsync(string name, int count = 36)
    {
        await Fixture.Database.DropCollectionAsync(name);
        var col = Fixture.Database.GetCollection<Note>(name);

        await col.InsertManyAsync(Enumerable.Range(1, count).Select(i => new Note
        {
            Id    = ObjectId.GenerateNewId(),
            Title = $"T{(i * 7) % 11:D2}",
            Owner = i % 3 == 0 ? null : $"u{i % 5}",
            Score = i % 4 == 0 ? null : i % 6,
            Seq   = i,
        }));

        return col;
    }

    private ISeekMongoBuilder<Note> Builder(string origin, IMongoCollection<Note> col, FilterDefinition<Note>? filter = null)
    {
        filter ??= FilterDefinition<Note>.Empty;
        return origin switch
        {
            "queryable" => Seek.CreateBuilder(col.AsQueryable().Where(n => filter.Inject())),
            "aggregate" => Seek.CreateBuilder(col.Aggregate().Match(filter)),
            _           => Seek.CreateBuilder(col.Find(filter)),
        };
    }

    // ── Nullable and string sort keys ─────────────────────────────────────────

    [Theory, MemberData(nameof(Origins))]
    public async Task NullableInt_Ascending(string origin)
    {
        var col = await SeedNotesAsync($"extras_nint_asc_{origin}");
        await AssertWalks(origin, col,
            b => b.OrderBy(n => n.Score).OrderBy(n => n.Id),
            f => f.SortBy(n => n.Score).ThenBy(n => n.Id));
    }

    [Theory, MemberData(nameof(Origins))]
    public async Task NullableInt_Descending(string origin)
    {
        var col = await SeedNotesAsync($"extras_nint_desc_{origin}");
        await AssertWalks(origin, col,
            b => b.OrderByDescending(n => n.Score).OrderBy(n => n.Id),
            f => f.SortByDescending(n => n.Score).ThenBy(n => n.Id));
    }

    [Theory, MemberData(nameof(Origins))]
    public async Task NullableString_Ascending(string origin)
    {
        var col = await SeedNotesAsync($"extras_nstr_asc_{origin}");
        await AssertWalks(origin, col,
            b => b.OrderBy(n => n.Owner).OrderBy(n => n.Id),
            f => f.SortBy(n => n.Owner).ThenBy(n => n.Id));
    }

    [Theory, MemberData(nameof(Origins))]
    public async Task NullableString_Descending(string origin)
    {
        var col = await SeedNotesAsync($"extras_nstr_desc_{origin}");
        await AssertWalks(origin, col,
            b => b.OrderByDescending(n => n.Owner).OrderByDescending(n => n.Id),
            f => f.SortByDescending(n => n.Owner).ThenByDescending(n => n.Id));
    }

    [Theory, MemberData(nameof(Origins))]
    public async Task NonNullString_WithDuplicates(string origin)
    {
        var col = await SeedNotesAsync($"extras_str_{origin}");
        await AssertWalks(origin, col,
            b => b.OrderByDescending(n => n.Title).OrderBy(n => n.Id),
            f => f.SortByDescending(n => n.Title).ThenBy(n => n.Id));
    }

    // ── Total count ───────────────────────────────────────────────────────────

    [Theory, MemberData(nameof(Origins))]
    public async Task WithTotalCount_CountsTheWholeFilteredQuery_OnEveryPage(string origin)
    {
        var col = await SeedNotesAsync($"extras_count_{origin}");
        var filter = Builders<Note>.Filter.Gt(n => n.Seq, 6);   // 30 of 36

        var first = await Builder(origin, col, filter)
            .WithRequest(new SeekRequest { PageSize = 8 })
            .OrderBy(n => n.Seq)
            .WithTotalCount()
            .ToSeekResultAsync();

        var second = await Builder(origin, col, filter)
            .WithRequest(new SeekRequest { Token = first.NextToken, PageSize = 8 })
            .OrderBy(n => n.Seq)
            .WithTotalCount()
            .ToSeekResultAsync();

        Assert.Equal(30, first.TotalCount);
        Assert.Equal(8, first.Count);
        Assert.Equal(30, second.TotalCount);
        Assert.Equal(Enumerable.Range(15, 8), second.Items.Select(n => n.Seq));
    }

    [Theory, MemberData(nameof(Origins))]
    public async Task WithTotalCount_OnAnEmptyResult_IsZero(string origin)
    {
        var col = await SeedNotesAsync($"extras_count0_{origin}");

        var page = await Builder(origin, col, Builders<Note>.Filter.Gt(n => n.Seq, 1000))
            .WithRequest(new SeekRequest { PageSize = 8 })
            .OrderBy(n => n.Seq)
            .WithTotalCount()
            .ToSeekResultAsync();

        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task TotalCount_IsNull_UnlessAskedFor()
    {
        var col = await SeedNotesAsync("extras_count_off");

        var page = await Builder("find", col)
            .WithRequest(new SeekRequest { PageSize = 8 })
            .OrderBy(n => n.Seq)
            .ToSeekResultAsync();

        Assert.Null(page.TotalCount);
    }

    // ── Walking every page ────────────────────────────────────────────────────

    [Theory, MemberData(nameof(Origins))]
    public async Task ToItemsAsync_YieldsEveryDocumentOnce_WithOneBuilder(string origin)
    {
        var col = await SeedNotesAsync($"extras_items_{origin}");
        var filter = Builders<Note>.Filter.Gt(n => n.Seq, 4);

        // One builder for the whole walk — the find builder in particular has to reset
        // the mutable IFindFluent between pages instead of stacking keyset filters.
        var seen = new List<int>();
        await foreach (var note in Builder(origin, col, filter)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderBy(n => n.Seq)
            .ToItemsAsync())
        {
            seen.Add(note.Seq);
        }

        Assert.Equal(Enumerable.Range(5, 32), seen);
    }

    [Theory, MemberData(nameof(Origins))]
    public async Task ToPagesAsync_WithTotalCount_ReportsItOnEveryPage(string origin)
    {
        var col = await SeedNotesAsync($"extras_pages_{origin}");

        var pages = new List<SeekResult<Note>>();
        await foreach (var page in Builder(origin, col)
            .WithRequest(new SeekRequest { PageSize = 10 })
            .OrderByDescending(n => n.Seq)
            .WithTotalCount()
            .ToPagesAsync())
        {
            pages.Add(page);
        }

        Assert.Equal([10, 10, 10, 6], pages.Select(p => p.Count));
        Assert.All(pages, p => Assert.Equal(36, p.TotalCount));
        Assert.Equal(Enumerable.Range(1, 36).Reverse(), pages.SelectMany(p => p.Items).Select(n => n.Seq));
    }

    // ── Client-driven sorting ─────────────────────────────────────────────────

    [Theory, MemberData(nameof(Origins))]
    public async Task ApplySort_PagesInTheRequestedOrder(string origin)
    {
        var col = await SeedNotesAsync($"extras_sort_{origin}");
        var expected = (await col.Find(FilterDefinition<Note>.Empty)
            .SortByDescending(n => n.Title).ThenBy(n => n.Score).ThenByDescending(n => n.Id)
            .ToListAsync()).Select(n => n.Seq).ToList();

        var seen = new List<int>();
        string? token = null;
        do
        {
            var page = await Builder(origin, col)
                .WithRequest(new SeekRequest { Token = token, PageSize = 7 })
                .ApplySort(Sorts, "title:desc,score:asc")
                .ToSeekResultAsync();

            seen.AddRange(page.Items.Select(n => n.Seq));
            token = page.NextToken;
        } while (token is not null);

        Assert.Equal(expected, seen);
    }

    [Fact]
    public async Task ApplySort_UnknownField_IsRejected()
    {
        var col = await SeedNotesAsync("extras_sort_bad");

        Assert.Throws<SeekSortException>(() => Builder("find", col).ApplySort(Sorts, "owner"));
    }

    // ── Computed sort keys ────────────────────────────────────────────────────

    [Theory]
    [InlineData("aggregate")]
    [InlineData("find")]
    public async Task ComputedSortKey_OnAggregateAndFind_FailsWithGuidance(string origin)
    {
        var col = await SeedNotesAsync($"extras_computed_{origin}");

        var ex = await Assert.ThrowsAsync<NotSupportedException>(async () => await Builder(origin, col)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderBy(n => n.Seq % 5)
            .OrderBy(n => n.Id)
            .ToSeekResultAsync());

        Assert.Contains("computed", ex.Message);
    }

    [Fact]
    public async Task ComputedSortKey_OnQueryable_PagesInOrder()
    {
        var col = await SeedNotesAsync("extras_computed_queryable");
        var expected = Enumerable.Range(1, 36).OrderBy(i => i % 5).ThenBy(i => i).ToList();

        var seen = new List<int>();
        await foreach (var note in Seek.CreateBuilder(col.AsQueryable())
            .WithRequest(new SeekRequest { PageSize = 7 })
            .OrderBy(n => n.Seq % 5)
            .OrderBy(n => n.Seq)
            .ToItemsAsync())
        {
            seen.Add(note.Seq);
        }

        Assert.Equal(expected, seen);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task AssertWalks(
        string origin,
        IMongoCollection<Note> col,
        Func<ISeekMongoBuilder<Note>, ISeekMongoBuilder<Note>> sort,
        Func<IFindFluent<Note, Note>, IFindFluent<Note, Note>> expectedSort,
        int pageSize = 7)
    {
        var expected = (await expectedSort(col.Find(FilterDefinition<Note>.Empty)).ToListAsync())
            .Select(n => n.Seq).ToList();

        ValueTask<SeekResult<Note>> Page(string? token)
            => sort(Builder(origin, col).WithRequest(new SeekRequest { Token = token, PageSize = pageSize }))
                .ToSeekResultAsync();

        // Forward: first page to last.
        var forward = new List<int>();
        SeekResult<Note> page;
        string? next = null;
        int guard = 0;
        do
        {
            page = await Page(next);
            forward.AddRange(page.Items.Select(n => n.Seq));
            next = page.NextToken;
            Assert.True(++guard <= 50, "runaway pagination (forward)");
        } while (next is not null);

        Assert.Equal(expected, forward);

        // Backward: from that last page to the first.
        var backward = new List<int>(page.Items.Select(n => n.Seq));
        var previous = page.PreviousToken;
        guard = 0;
        while (previous is not null)
        {
            page = await Page(previous);
            backward.InsertRange(0, page.Items.Select(n => n.Seq));
            previous = page.PreviousToken;
            Assert.True(++guard <= 50, "runaway pagination (backward)");
        }

        Assert.Equal(expected, backward);
    }
}
