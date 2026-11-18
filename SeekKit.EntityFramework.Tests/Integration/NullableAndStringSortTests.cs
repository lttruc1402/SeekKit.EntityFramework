using SeekKit.EntityFramework.Builders;

namespace SeekKit.EntityFramework.Tests.Integration;

/// <summary>
/// Sort columns that used to break past the first page: nullable columns (rows holding
/// NULL were skipped), and string columns (no &gt;/&lt; operator to build the filter from).
/// Every case pages the whole table forward and then all the way back, and compares both
/// walks with the same query run unpaged — so the database's own ORDER BY, including
/// where it puts NULL, is the oracle.
/// </summary>
public sealed class NullableAndStringSortTests : IClassFixture<SeekFixture>, IDisposable
{
    private readonly ISeekFactory  _factory;
    private readonly TestDbContext _db;
    private readonly ISeekSerializer _serializer;

    public NullableAndStringSortTests(SeekFixture fixture)
    {
        _factory    = fixture.Factory;
        _serializer = fixture.ServiceProvider.GetRequiredService<ISeekSerializer>();
        _db      = new TestDbContext();
        _db.Database.EnsureCreated();
        _db.Seed(count: 30);
        _db.SeedTickets(count: 40);
    }

    public static TheoryData<string> Strategies => new() { "union", "or" };

    [Theory, MemberData(nameof(Strategies))]
    public Task NullableInt_Ascending(string strategy) => AssertTicketWalks(strategy,
        b => b.OrderBy(t => t.Priority).OrderBy(t => t.Id),
        q => q.OrderBy(t => t.Priority).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NullableInt_Descending(string strategy) => AssertTicketWalks(strategy,
        b => b.OrderByDescending(t => t.Priority).OrderBy(t => t.Id),
        q => q.OrderByDescending(t => t.Priority).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NullableString_Ascending(string strategy) => AssertTicketWalks(strategy,
        b => b.OrderBy(t => t.Assignee).OrderBy(t => t.Id),
        q => q.OrderBy(t => t.Assignee).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NullableString_Descending(string strategy) => AssertTicketWalks(strategy,
        b => b.OrderByDescending(t => t.Assignee).OrderByDescending(t => t.Id),
        q => q.OrderByDescending(t => t.Assignee).ThenByDescending(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NullableDate_Descending(string strategy) => AssertTicketWalks(strategy,
        b => b.OrderByDescending(t => t.DueAt).OrderBy(t => t.Id),
        q => q.OrderByDescending(t => t.DueAt).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task TwoNullableColumns_MixedDirections(string strategy) => AssertTicketWalks(strategy,
        b => b.OrderByDescending(t => t.Priority).OrderBy(t => t.Assignee).OrderBy(t => t.Id),
        q => q.OrderByDescending(t => t.Priority).ThenBy(t => t.Assignee).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NonNullString_WithDuplicates_Ascending(string strategy) => AssertTicketWalks(strategy,
        b => b.OrderBy(t => t.Title).OrderBy(t => t.Id),
        q => q.OrderBy(t => t.Title).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NonNullString_WithDuplicates_Descending(string strategy) => AssertTicketWalks(strategy,
        b => b.OrderByDescending(t => t.Title).OrderBy(t => t.Id),
        q => q.OrderByDescending(t => t.Title).ThenBy(t => t.Id));

    [Fact]
    public async Task NullableOnlySort_LastRowIsNull_NextPageIsEmptyNotTheWholeTable()
    {
        // Sorted descending on SQLite the NULLs come last, so a boundary of NULL is the
        // end of the scan: nothing lies beyond it. The filter must say "no rows", never
        // "no filter".
        var nullPriorityIds = await _db.Tickets.Where(t => t.Priority == null).Select(t => t.Id).ToListAsync();

        var token = _serializer.Serialize(new SeekData
        {
            Direction = SeekDirection.Next,
            Values = new Dictionary<string, string> { ["Priority"] = null! },
        });

        var page = await _factory
            .CreateBuilder(_db.Tickets)
            .WithRequest(new SeekRequest { Token = token, PageSize = 50 })
            .OrderByDescending(t => t.Priority)
            .ToSeekResultAsync();

        Assert.NotEmpty(nullPriorityIds);
        Assert.Empty(page.Items);
        Assert.False(page.HasNext);
    }

    [Fact]
    public async Task ProductName_StringSort_ContinuesPastFirstPage()
    {
        // The original gap: OrderBy(p => p.Name) returned page 1 and threw on page 2.
        var expected = await _db.Products.OrderByDescending(p => p.Name).ThenBy(p => p.Id).Select(p => p.Id).ToListAsync();

        var seen = new List<int>();
        string? token = null;
        do
        {
            var page = await _factory
                .CreateBuilder(_db.Products)
                .WithRequest(new SeekRequest { Token = token, PageSize = 8 })
                .OrderByDescending(p => p.Name)
                .OrderBy(p => p.Id)
                .ToSeekResultAsync();

            seen.AddRange(page.Items.Select(p => p.Id));
            token = page.NextToken;
        } while (token is not null);

        Assert.Equal(expected, seen);
    }

    private async Task AssertTicketWalks(
        string strategy,
        Func<ISeekBuilder<Ticket>, ISeekBuilder<Ticket>> sort,
        Func<IQueryable<Ticket>, IQueryable<Ticket>> expectedOrder,
        int pageSize = 7)
    {
        var expected = await expectedOrder(_db.Tickets).Select(t => t.Id).ToListAsync();

        ISeekBuilder<Ticket> Build(string? token)
        {
            var builder = _factory
                .CreateBuilder(_db.Tickets)
                .WithRequest(new SeekRequest { Token = token, PageSize = pageSize });
            builder = strategy == "or" ? builder.WithOrPredicate() : builder.WithUnionAll();
            return sort(builder);
        }

        // Forward: first page to last.
        var forward = new List<int>();
        SeekResult<Ticket> page;
        string? next = null;
        int guard = 0;
        do
        {
            page = await Build(next).ToSeekResultAsync();
            forward.AddRange(page.Items.Select(t => t.Id));
            next = page.NextToken;
            Assert.True(++guard <= 50, "runaway pagination (forward)");
        } while (next is not null);

        Assert.Equal(expected, forward);

        // Backward: from that last page to the first.
        var backward = new List<int>(page.Items.Select(t => t.Id));
        var previous = page.PreviousToken;
        guard = 0;
        while (previous is not null)
        {
            page = await Build(previous).ToSeekResultAsync();
            backward.InsertRange(0, page.Items.Select(t => t.Id));
            previous = page.PreviousToken;
            Assert.True(++guard <= 50, "runaway pagination (backward)");
        }

        Assert.Equal(expected, backward);
    }

    public void Dispose() => _db.Dispose();
}
