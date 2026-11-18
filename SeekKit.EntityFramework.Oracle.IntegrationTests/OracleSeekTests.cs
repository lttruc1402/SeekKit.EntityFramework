using Microsoft.EntityFrameworkCore;
using SeekKit.EntityFramework.Core;
using SeekKit.EntityFramework.IntegrationTests.Shared;
using Testcontainers.Oracle;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SeekKit.EntityFramework.Oracle.IntegrationTests;

/// <summary>
/// Starts a real Oracle Database 23 Free in a throwaway container (via Testcontainers)
/// once for the whole suite. Like PostgreSQL, Oracle sorts <c>NULL</c> last in ascending
/// order.
/// </summary>
public sealed class OracleFixture : IAsyncLifetime, IRelationalFixture
{
    private readonly OracleContainer _container = new OracleBuilder("gvenzl/oracle-free:23-slim-faststart").Build();
    private DbContextOptions<TicketDbContext> _options = null!;

    public DatabaseStrategy Strategy { get; } = DatabaseStrategy.ForOracle();

    public bool NullsLastAscending => true;

    public TicketDbContext CreateContext() => new(_options);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _options = new DbContextOptionsBuilder<TicketDbContext>().UseOracle(_container.GetConnectionString()).Options;

        await using var db = CreateContext();
        await db.CreateAndSeedAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("oracle")]
public sealed class OracleCollection : ICollectionFixture<OracleFixture> { }

/// <summary>The shared end-to-end suite, run against a real Oracle Database 23 Free.</summary>
[Collection("oracle")]
public sealed class OracleSeekTests(OracleFixture fixture) : RelationalSeekTests(fixture);
