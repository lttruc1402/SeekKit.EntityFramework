using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Testcontainers.PostgreSql;
using Xunit;

// One shared PostgreSQL container for the whole assembly; run test classes
// sequentially so they don't contend on it.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SeekKit.EntityFramework.PostgreSql.IntegrationTests;

/// <summary>
/// Starts a real PostgreSQL in a throwaway container (via Testcontainers) once for all
/// tests in the collection, creates the schema, and seeds it. PostgreSQL is the one
/// provider whose behaviour differs from the SQLite database the unit tests run on in
/// two ways that matter to SeekKit: it sorts <c>NULL</c> <em>last</em> in ascending
/// order, and it has the row-value comparison <c>(a, b) &gt; (@a, @b)</c>.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const int TicketCount = 60;

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine").Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();

        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync();

        var day = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        db.Tickets.AddRange(Enumerable.Range(1, TicketCount).Select(i => new Ticket
        {
            Id        = i,
            Title     = $"Title {(i * 7) % 11:D2}",
            Assignee  = i % 3 == 0 ? null : $"user{i % 6}",
            Priority  = i % 5 == 0 ? null : i % 4,
            DueAt     = i % 4 == 0 ? null : day.AddDays(i % 7),
            CreatedAt = day.AddMinutes(i / 2),          // pairs of rows share a timestamp
            Amount    = (i * 37) % 50 + 0.25m,
        }));
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public TicketDbContext CreateContext(SqlRecorder? recorder = null)
    {
        var options = new DbContextOptionsBuilder<TicketDbContext>().UseNpgsql(ConnectionString);
        if (recorder is not null)
            options.AddInterceptors(recorder);
        return new TicketDbContext(options.Options);
    }
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }

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
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Ticket>(e =>
        {
            e.Property(t => t.Id).ValueGeneratedNever();
            e.Property(t => t.Amount).HasPrecision(18, 2);
        });
    }
}

/// <summary>Records the SQL text of every command EF Core sends, so tests can assert on its shape.</summary>
public sealed class SqlRecorder : DbCommandInterceptor
{
    public List<string> Commands { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Commands.Add(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Commands.Add(command.CommandText);
        return base.ReaderExecuting(command, eventData, result);
    }
}
