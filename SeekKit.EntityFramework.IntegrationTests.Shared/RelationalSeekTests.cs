using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SeekKit.Core;
using SeekKit.Core.Models;
using SeekKit.EntityFramework.Builders;
using SeekKit.EntityFramework.Core;
using Xunit;

namespace SeekKit.EntityFramework.IntegrationTests.Shared;

/// <summary>
/// What a provider-specific test project supplies: a way to open a context on its real
/// database, the SeekKit strategy configured for it, and where it sorts <c>NULL</c>.
/// </summary>
public interface IRelationalFixture
{
    TicketDbContext CreateContext();

    /// <summary>The <see cref="DatabaseStrategy"/> an application would configure for this database.</summary>
    DatabaseStrategy Strategy { get; }

    /// <summary>
    /// Whether this database returns <c>NULL</c> after every other value for
    /// <c>ORDER BY col ASC</c>. Stated by the test project and checked against the real
    /// database by <see cref="RelationalSeekTests.Database_SortsNull_WhereSeekKitAssumes"/>.
    /// </summary>
    bool NullsLastAscending { get; }
}

/// <summary>
/// A row with nullable sort columns of every flavour (value type, reference type, date),
/// a non-null string with duplicates, a non-unique timestamp, and a decimal.
/// </summary>
public sealed class Ticket
{
    public int       Id        { get; set; }
    public string    Title     { get; set; } = "";
    public string?   Assignee  { get; set; }
    public int?      Priority  { get; set; }
    public DateTime? DueAt     { get; set; }
    public DateTime  CreatedAt { get; set; }
    public decimal   Amount    { get; set; }
}

public sealed class TicketDbContext(DbContextOptions<TicketDbContext> options) : DbContext(options)
{
    public const int TicketCount = 60;

    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ticket>(e =>
        {
            e.Property(t => t.Id).ValueGeneratedNever();
            e.Property(t => t.Title).HasMaxLength(64);
            e.Property(t => t.Assignee).HasMaxLength(64);
            e.Property(t => t.Amount).HasPrecision(18, 2);
        });
    }

    /// <summary>Creates the schema and the 60 tickets every test reads.</summary>
    public async Task CreateAndSeedAsync()
    {
        await Database.EnsureCreatedAsync();

        // Unspecified kind: not every provider stores a time zone, and the same values
        // have to round-trip through all of them.
        var day = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Unspecified);
        Tickets.AddRange(Enumerable.Range(1, TicketCount).Select(i => new Ticket
        {
            Id        = i,
            Title     = $"Title {(i * 7) % 11:D2}",
            Assignee  = i % 3 == 0 ? null : $"user{i % 6}",
            Priority  = i % 5 == 0 ? null : i % 4,
            DueAt     = i % 4 == 0 ? null : day.AddDays(i % 7),
            CreatedAt = day.AddMinutes(i / 2),          // pairs of rows share a timestamp
            Amount    = (i * 37) % 50 + 0.25m,
        }));
        await SaveChangesAsync();
        ChangeTracker.Clear();
    }
}

/// <summary>
/// The provider-independent end-to-end suite. Every walk pages the whole table forward and
/// then all the way back and is compared with the same sort run unpaged — so the
/// database's own <c>ORDER BY</c>, including where it puts <c>NULL</c> and how its
/// collation orders strings, is the oracle. Each case runs under the database's default
/// strategy and under UNION ALL and the OR predicate explicitly.
/// </summary>
public abstract class RelationalSeekTests
{
    private static readonly SeekSortMap<Ticket> Sorts = new SeekSortMap<Ticket>()
        .Add("title", t => t.Title)
        .Add("priority", t => t.Priority)
        .Add("createdAt", t => t.CreatedAt)
        .Add("amount", t => t.Amount)
        .Add("id", t => t.Id)
        .Default("createdAt:desc")
        .TieBreaker("id");

    private readonly IRelationalFixture _fixture;
    private readonly ISeekFactory _factory;

    protected RelationalSeekTests(IRelationalFixture fixture)
    {
        _fixture = fixture;

        var services = new ServiceCollection();
        services.AddSeekKit(o => o.Strategy = fixture.Strategy);
        _factory = services.BuildServiceProvider().GetRequiredService<ISeekFactory>();
    }

    public static TheoryData<string> Strategies => new() { "default", "union", "or" };

    private ISeekBuilder<Ticket> Builder(IQueryable<Ticket> query, string strategy)
    {
        var builder = _factory.CreateBuilder(query);
        return strategy switch
        {
            "union" => builder.WithUnionAll(),
            "or"    => builder.WithOrPredicate(),
            _       => builder,
        };
    }

    // ── The premise ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Database_SortsNull_WhereSeekKitAssumes()
    {
        await using var db = _fixture.CreateContext();

        var ascending = await db.Tickets.OrderBy(t => t.Priority).Select(t => t.Priority).ToListAsync();

        // Documents what this database really does. That SeekKit's default for the provider
        // agrees with it is what every nullable walk below proves: with the wrong
        // assumption they skip or repeat the NULL rows.
        bool nullsLast = ascending[^1] is null;
        Assert.Equal(_fixture.NullsLastAscending, nullsLast);
        Assert.True(nullsLast ? ascending[0] is not null : ascending[0] is null);
    }

    // ── Nullable sort columns ─────────────────────────────────────────────────

    [Theory, MemberData(nameof(Strategies))]
    public Task NullableInt_Ascending(string strategy) => AssertWalks(strategy,
        b => b.OrderBy(t => t.Priority).OrderBy(t => t.Id),
        q => q.OrderBy(t => t.Priority).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NullableInt_Descending(string strategy) => AssertWalks(strategy,
        b => b.OrderByDescending(t => t.Priority).OrderByDescending(t => t.Id),
        q => q.OrderByDescending(t => t.Priority).ThenByDescending(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NullableString_Ascending(string strategy) => AssertWalks(strategy,
        b => b.OrderBy(t => t.Assignee).OrderBy(t => t.Id),
        q => q.OrderBy(t => t.Assignee).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NullableString_Descending(string strategy) => AssertWalks(strategy,
        b => b.OrderByDescending(t => t.Assignee).OrderBy(t => t.Id),
        q => q.OrderByDescending(t => t.Assignee).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NullableTimestamp_Descending(string strategy) => AssertWalks(strategy,
        b => b.OrderByDescending(t => t.DueAt).OrderBy(t => t.Id),
        q => q.OrderByDescending(t => t.DueAt).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task TwoNullableColumns_MixedDirections(string strategy) => AssertWalks(strategy,
        b => b.OrderByDescending(t => t.Priority).OrderBy(t => t.Assignee).OrderBy(t => t.Id),
        q => q.OrderByDescending(t => t.Priority).ThenBy(t => t.Assignee).ThenBy(t => t.Id));

    // ── Non-null columns: timestamps, decimals, strings ───────────────────────

    [Theory, MemberData(nameof(Strategies))]
    public Task Timestamp_ThenId_SameDirection(string strategy) => AssertWalks(strategy,
        b => b.OrderByDescending(t => t.CreatedAt).OrderByDescending(t => t.Id),
        q => q.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task Decimal_ThenId_MixedDirections(string strategy) => AssertWalks(strategy,
        b => b.OrderByDescending(t => t.Amount).OrderBy(t => t.Id),
        q => q.OrderByDescending(t => t.Amount).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NonNullString_WithDuplicates_Ascending(string strategy) => AssertWalks(strategy,
        b => b.OrderBy(t => t.Title).OrderBy(t => t.Id),
        q => q.OrderBy(t => t.Title).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NonNullString_WithDuplicates_Descending(string strategy) => AssertWalks(strategy,
        b => b.OrderByDescending(t => t.Title).OrderByDescending(t => t.Id),
        q => q.OrderByDescending(t => t.Title).ThenByDescending(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task ComputedStringExpression(string strategy) => AssertWalks(strategy,
        b => b.OrderBy(t => t.Title.ToLower()).OrderBy(t => t.Id),
        q => q.OrderBy(t => t.Title.ToLower()).ThenBy(t => t.Id));

    // ── The rest of the surface, once each ────────────────────────────────────

    [Theory, MemberData(nameof(Strategies))]
    public async Task ApplySort_ClientDrivenSort(string strategy)
    {
        await using var db = _fixture.CreateContext();
        var expected = await db.Tickets
            .OrderByDescending(t => t.Priority).ThenBy(t => t.Title).ThenByDescending(t => t.Id)
            .Select(t => t.Id).ToListAsync();

        var seen = new List<int>();
        string? token = null;
        do
        {
            var page = await Builder(db.Tickets, strategy)
                .WithRequest(new SeekRequest { Token = token, PageSize = 9 })
                .ApplySort(Sorts, "priority:desc,title:asc")
                .ToSeekResultAsync();

            seen.AddRange(page.Items.Select(t => t.Id));
            token = page.NextToken;
        } while (token is not null);

        Assert.Equal(expected, seen);
    }

    [Fact]
    public async Task TotalCount_AndStreaming()
    {
        await using var db = _fixture.CreateContext();
        var withAssignee = db.Tickets.Where(t => t.Assignee != null);
        var expected = await withAssignee.OrderBy(t => t.DueAt).ThenBy(t => t.Id).Select(t => t.Id).ToListAsync();

        var pages = new List<SeekResult<Ticket>>();
        await foreach (var page in _factory
            .CreateBuilder(withAssignee)
            .WithRequest(new SeekRequest { PageSize = 11 })
            .OrderBy(t => t.DueAt)
            .OrderBy(t => t.Id)
            .WithTotalCount()
            .ToPagesAsync())
        {
            pages.Add(page);
        }

        Assert.Equal(expected, pages.SelectMany(p => p.Items).Select(t => t.Id));
        Assert.All(pages, p => Assert.Equal(expected.Count, p.TotalCount));
    }

    [Fact]
    public async Task Projection_ReadsANullCursorValueBackFromTheResult()
    {
        await using var db = _fixture.CreateContext();
        var expected = await db.Tickets.OrderBy(t => t.Priority).ThenBy(t => t.Id).Select(t => t.Id).ToListAsync();

        var seen = new List<int>();
        await foreach (var item in _factory
            .CreateBuilder(db.Tickets)
            .WithRequest(new SeekRequest { PageSize = 7 })
            .OrderBy(t => t.Priority)
            .OrderBy(t => t.Id)
            .Select(q => q.Select(t => new { Key = t.Id, Level = t.Priority, t.Title }))   // both renamed
            .ToItemsAsync())
        {
            seen.Add(item.Key);
        }

        Assert.Equal(expected, seen);
    }

    [Fact]
    public async Task TokenFromAnotherSort_IsRejected()
    {
        await using var db = _fixture.CreateContext();

        var first = await _factory.CreateBuilder(db.Tickets)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderBy(t => t.CreatedAt).OrderBy(t => t.Id)
            .ToSeekResultAsync();

        var ex = await Assert.ThrowsAsync<SeekTokenException>(async () => await _factory
            .CreateBuilder(db.Tickets)
            .WithRequest(new SeekRequest { Token = first.NextToken, PageSize = 5 })
            .OrderByDescending(t => t.CreatedAt).OrderByDescending(t => t.Id)
            .ToSeekResultAsync());

        Assert.Equal(SeekTokenError.SortMismatch, ex.Error);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task AssertWalks(
        string strategy,
        Func<ISeekBuilder<Ticket>, ISeekBuilder<Ticket>> sort,
        Func<IQueryable<Ticket>, IQueryable<Ticket>> expectedOrder,
        int pageSize = 7)
    {
        await using var db = _fixture.CreateContext();

        var expected = await expectedOrder(db.Tickets).Select(t => t.Id).ToListAsync();
        Assert.Equal(TicketDbContext.TicketCount, expected.Count);

        ValueTask<SeekResult<Ticket>> Page(string? token)
            => sort(Builder(db.Tickets.AsNoTracking(), strategy).WithRequest(new SeekRequest { Token = token, PageSize = pageSize }))
                .ToSeekResultAsync();

        // Forward: first page to last.
        var forward = new List<int>();
        SeekResult<Ticket> page;
        string? next = null;
        int guard = 0;
        do
        {
            page = await Page(next);
            forward.AddRange(page.Items.Select(t => t.Id));
            next = page.NextToken;
            Assert.True(++guard <= 100, "runaway pagination (forward)");
        } while (next is not null);

        Assert.Equal(expected, forward);

        // Backward: from that last page to the first.
        var backward = new List<int>(page.Items.Select(t => t.Id));
        var previous = page.PreviousToken;
        guard = 0;
        while (previous is not null)
        {
            page = await Page(previous);
            backward.InsertRange(0, page.Items.Select(t => t.Id));
            previous = page.PreviousToken;
            Assert.True(++guard <= 100, "runaway pagination (backward)");
        }

        Assert.Equal(expected, backward);
    }
}
