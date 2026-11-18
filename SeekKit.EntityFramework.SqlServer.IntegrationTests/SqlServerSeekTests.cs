using Microsoft.EntityFrameworkCore;
using SeekKit.EntityFramework.Core;
using SeekKit.EntityFramework.IntegrationTests.Shared;
using Testcontainers.MsSql;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SeekKit.EntityFramework.SqlServer.IntegrationTests;

/// <summary>
/// Starts a real SQL Server 2022 in a throwaway container (via Testcontainers) once for
/// the whole suite. SQL Server sorts <c>NULL</c> first in ascending order.
/// </summary>
public sealed class SqlServerFixture : IAsyncLifetime, IRelationalFixture
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private DbContextOptions<TicketDbContext> _options = null!;

    public DatabaseStrategy Strategy { get; } = DatabaseStrategy.ForSqlServer();

    public bool NullsLastAscending => false;

    public TicketDbContext CreateContext() => new(_options);

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _options = new DbContextOptionsBuilder<TicketDbContext>().UseSqlServer(_container.GetConnectionString()).Options;

        await using var db = CreateContext();
        await db.CreateAndSeedAsync();
    }

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition("sqlserver")]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture> { }

/// <summary>The shared end-to-end suite, run against a real SQL Server 2022.</summary>
[Collection("sqlserver")]
public sealed class SqlServerSeekTests(SqlServerFixture fixture) : RelationalSeekTests(fixture);
