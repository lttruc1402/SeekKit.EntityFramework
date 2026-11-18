using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SeekKit.Core;
using SeekKit.Core.Models;
using SeekKit.EntityFramework.Builders;
using SeekKit.EntityFramework.Core;
using Xunit;

namespace SeekKit.EntityFramework.PostgreSql.IntegrationTests;

/// <summary>
/// End-to-end tests against a real PostgreSQL. Every walk pages the whole table forward
/// and then all the way back and is compared with the same sort run unpaged, so
/// PostgreSQL's own <c>ORDER BY</c> — including <c>NULLS LAST</c> for ascending — is the
/// oracle. Each case runs under all four PostgreSQL strategies.
/// </summary>
[Collection("postgres")]
public sealed partial class PostgresSeekTests
{
    private static readonly SeekSortMap<Ticket> Sorts = new SeekSortMap<Ticket>()
        .Add("title", t => t.Title)
        .Add("priority", t => t.Priority)
        .Add("createdAt", t => t.CreatedAt)
        .Add("amount", t => t.Amount)
        .Add("id", t => t.Id)
        .Default("createdAt:desc")
        .TieBreaker("id");

    private readonly PostgresFixture _fixture;

    public PostgresSeekTests(PostgresFixture fixture) => _fixture = fixture;

    public static TheoryData<PostgreSqlStrategy> Strategies => new()
    {
        PostgreSqlStrategy.Auto,
        PostgreSqlStrategy.Tuple,
        PostgreSqlStrategy.UnionAll,
        PostgreSqlStrategy.OrLogic,
    };

    private static ISeekFactory Factory(PostgreSqlStrategy strategy, Action<SeekKitOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSeekKit(o =>
        {
            o.Strategy = DatabaseStrategy.ForPostgreSql(strategy);
            configure?.Invoke(o);
        });
        return services.BuildServiceProvider().GetRequiredService<ISeekFactory>();
    }

    // ── The premise ───────────────────────────────────────────────────────────

    [Fact]
    public async Task PostgreSql_SortsNullLast_Ascending_AndFirst_Descending()
    {
        // If this ever stops holding, NullsLargest is the wrong default for PostgreSQL.
        await using var db = _fixture.CreateContext();

        var ascending  = await db.Tickets.OrderBy(t => t.Priority).Select(t => t.Priority).ToListAsync();
        var descending = await db.Tickets.OrderByDescending(t => t.Priority).Select(t => t.Priority).ToListAsync();

        Assert.NotNull(ascending[0]);
        Assert.Null(ascending[^1]);
        Assert.Null(descending[0]);
        Assert.NotNull(descending[^1]);
    }

    // ── Nullable sort columns ─────────────────────────────────────────────────

    [Theory, MemberData(nameof(Strategies))]
    public Task NullableInt_Ascending(PostgreSqlStrategy strategy) => AssertWalks(strategy,
        b => b.OrderBy(t => t.Priority).OrderBy(t => t.Id),
        q => q.OrderBy(t => t.Priority).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NullableInt_Descending(PostgreSqlStrategy strategy) => AssertWalks(strategy,
        b => b.OrderByDescending(t => t.Priority).OrderByDescending(t => t.Id),
        q => q.OrderByDescending(t => t.Priority).ThenByDescending(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NullableString_Ascending(PostgreSqlStrategy strategy) => AssertWalks(strategy,
        b => b.OrderBy(t => t.Assignee).OrderBy(t => t.Id),
        q => q.OrderBy(t => t.Assignee).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NullableString_Descending(PostgreSqlStrategy strategy) => AssertWalks(strategy,
        b => b.OrderByDescending(t => t.Assignee).OrderBy(t => t.Id),
        q => q.OrderByDescending(t => t.Assignee).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NullableTimestamp_Descending(PostgreSqlStrategy strategy) => AssertWalks(strategy,
        b => b.OrderByDescending(t => t.DueAt).OrderBy(t => t.Id),
        q => q.OrderByDescending(t => t.DueAt).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task TwoNullableColumns_MixedDirections(PostgreSqlStrategy strategy) => AssertWalks(strategy,
        b => b.OrderByDescending(t => t.Priority).OrderBy(t => t.Assignee).OrderBy(t => t.Id),
        q => q.OrderByDescending(t => t.Priority).ThenBy(t => t.Assignee).ThenBy(t => t.Id));

    // ── Non-null columns: timestamps, decimals, strings ───────────────────────

    [Theory, MemberData(nameof(Strategies))]
    public Task Timestamp_ThenId_SameDirection(PostgreSqlStrategy strategy) => AssertWalks(strategy,
        b => b.OrderByDescending(t => t.CreatedAt).OrderByDescending(t => t.Id),
        q => q.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task Decimal_ThenId_MixedDirections(PostgreSqlStrategy strategy) => AssertWalks(strategy,
        b => b.OrderByDescending(t => t.Amount).OrderBy(t => t.Id),
        q => q.OrderByDescending(t => t.Amount).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NonNullString_WithDuplicates_Ascending(PostgreSqlStrategy strategy) => AssertWalks(strategy,
        b => b.OrderBy(t => t.Title).OrderBy(t => t.Id),
        q => q.OrderBy(t => t.Title).ThenBy(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task NonNullString_WithDuplicates_Descending(PostgreSqlStrategy strategy) => AssertWalks(strategy,
        b => b.OrderByDescending(t => t.Title).OrderByDescending(t => t.Id),
        q => q.OrderByDescending(t => t.Title).ThenByDescending(t => t.Id));

    [Theory, MemberData(nameof(Strategies))]
    public Task ComputedStringExpression(PostgreSqlStrategy strategy) => AssertWalks(strategy,
        b => b.OrderBy(t => t.Title.ToLower()).OrderBy(t => t.Id),
        q => q.OrderBy(t => t.Title.ToLower()).ThenBy(t => t.Id));

    // ── Which SQL each strategy produces ──────────────────────────────────────

    [Theory]
    [InlineData(PostgreSqlStrategy.Auto)]
    [InlineData(PostgreSqlStrategy.Tuple)]
    public async Task NonNullSameDirectionSort_UsesRowValueComparison(PostgreSqlStrategy strategy)
    {
        var sql = await SecondPageSql(strategy, b => b.OrderBy(t => t.CreatedAt).OrderBy(t => t.Id));

        Assert.Matches(RowValueComparison(), sql);
        // The boundary values are parameters, not literals — one cached plan serves every page.
        Assert.Contains("@", sql);
        Assert.DoesNotContain("2024-", sql);
    }

    [Theory]
    [InlineData(PostgreSqlStrategy.Auto)]
    [InlineData(PostgreSqlStrategy.Tuple)]
    public async Task SortWithANullableColumn_FallsBackFromRowValueComparison(PostgreSqlStrategy strategy)
    {
        // (a, b) > (@a, @b) is unknown as soon as a is NULL, so it would drop those rows.
        var sql = await SecondPageSql(strategy, b => b.OrderBy(t => t.Priority).OrderBy(t => t.Id));

        Assert.DoesNotMatch(RowValueComparison(), sql);
        Assert.Contains("IS NULL", sql);

        // ...and the default fallback is UNION ALL with the IS NULL alternative as a
        // branch of its own. An OR — as a strategy or inside a branch — makes PostgreSQL
        // read and sort every row beyond the cursor (measured: 1.3 s vs 1.4 ms).
        Assert.Contains("UNION ALL", sql);
        Assert.DoesNotContain(" OR ", sql);
    }

    [Fact]
    public async Task NonNullAnnotatedString_StillUsesRowValueComparison()
    {
        // `string Title` is annotated non-null, so it must not be pushed off the fast path.
        var sql = await SecondPageSql(PostgreSqlStrategy.Auto, b => b.OrderBy(t => t.Title).OrderBy(t => t.Id));

        Assert.Matches(RowValueComparison(), sql);
    }

    // ── The rest of the surface, once each, on PostgreSQL ─────────────────────

    [Theory, MemberData(nameof(Strategies))]
    public async Task ApplySort_ClientDrivenSort(PostgreSqlStrategy strategy)
    {
        await using var db = _fixture.CreateContext();
        var expected = await db.Tickets
            .OrderByDescending(t => t.Priority).ThenBy(t => t.Title).ThenByDescending(t => t.Id)
            .Select(t => t.Id).ToListAsync();

        var seen = new List<int>();
        string? token = null;
        do
        {
            var page = await Factory(strategy)
                .CreateBuilder(db.Tickets)
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
        await foreach (var page in Factory(PostgreSqlStrategy.Auto)
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
        await foreach (var item in Factory(PostgreSqlStrategy.Auto)
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
        var factory = Factory(PostgreSqlStrategy.Auto);

        var first = await factory.CreateBuilder(db.Tickets)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderBy(t => t.CreatedAt).OrderBy(t => t.Id)
            .ToSeekResultAsync();

        var ex = await Assert.ThrowsAsync<SeekTokenException>(async () => await factory
            .CreateBuilder(db.Tickets)
            .WithRequest(new SeekRequest { Token = first.NextToken, PageSize = 5 })
            .OrderByDescending(t => t.CreatedAt).OrderByDescending(t => t.Id)
            .ToSeekResultAsync());

        Assert.Equal(SeekTokenError.SortMismatch, ex.Error);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // (a, b) > (@a, @b): a parenthesised list, a comparison, another parenthesised list.
    [GeneratedRegex(@"\([^()]*,[^()]*\)\s*[<>]\s*\(")]
    private static partial Regex RowValueComparison();

    private async Task<string> SecondPageSql(PostgreSqlStrategy strategy, Func<ISeekBuilder<Ticket>, ISeekBuilder<Ticket>> sort)
    {
        var recorder = new SqlRecorder();
        await using var db = _fixture.CreateContext(recorder);
        var factory = Factory(strategy);

        var first = await sort(factory.CreateBuilder(db.Tickets).WithRequest(new SeekRequest { PageSize = 5 })).ToSeekResultAsync();
        recorder.Commands.Clear();

        var second = await sort(factory.CreateBuilder(db.Tickets).WithRequest(new SeekRequest { Token = first.NextToken, PageSize = 5 })).ToSeekResultAsync();

        Assert.Equal(5, second.Count);
        return Assert.Single(recorder.Commands);
    }

    private async Task AssertWalks(
        PostgreSqlStrategy strategy,
        Func<ISeekBuilder<Ticket>, ISeekBuilder<Ticket>> sort,
        Func<IQueryable<Ticket>, IQueryable<Ticket>> expectedOrder,
        int pageSize = 7)
    {
        await using var db = _fixture.CreateContext();
        var factory = Factory(strategy);

        var expected = await expectedOrder(db.Tickets).Select(t => t.Id).ToListAsync();
        Assert.Equal(PostgresFixture.TicketCount, expected.Count);

        ValueTask<SeekResult<Ticket>> Page(string? token)
            => sort(factory.CreateBuilder(db.Tickets).WithRequest(new SeekRequest { Token = token, PageSize = pageSize }))
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
