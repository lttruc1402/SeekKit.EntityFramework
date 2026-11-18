using Microsoft.EntityFrameworkCore;
using SeekKit.EntityFramework.Core;
using SeekKit.EntityFramework.IntegrationTests.Shared;
using Testcontainers.MySql;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SeekKit.EntityFramework.MySql.IntegrationTests;

/// <summary>
/// Starts a real MySQL in a throwaway container (via Testcontainers) once for the whole
/// suite. MySQL sorts <c>NULL</c> first in ascending order and compares strings with a
/// case-insensitive collation by default.
/// </summary>
public sealed class MySqlFixture : IAsyncLifetime, IRelationalFixture
{
    private readonly MySqlContainer _container = new MySqlBuilder("mysql:8.4").Build();
    private DbContextOptions<TicketDbContext> _options = null!;

    public DatabaseStrategy Strategy { get; } = DatabaseStrategy.ForMySql();

    public bool NullsLastAscending => false;

    public TicketDbContext CreateContext() => new(_options);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _options = new DbContextOptionsBuilder<TicketDbContext>().UseMySQL(_container.GetConnectionString()).Options;

        await using var db = CreateContext();
        await db.CreateAndSeedAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("mysql")]
public sealed class MySqlCollection : ICollectionFixture<MySqlFixture> { }

/// <summary>The shared end-to-end suite, run against a real MySQL 8.4.</summary>
[Collection("mysql")]
public sealed class MySqlSeekTests(MySqlFixture fixture) : RelationalSeekTests(fixture);
