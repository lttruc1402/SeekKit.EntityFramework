namespace SeekKit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// EF Core DbContext backed by a SQLite in-memory database.
/// The <see cref="SqliteConnection"/> is kept open for the lifetime of this context
/// so the in-memory database survives across multiple SaveChanges / query calls.
/// </summary>
public sealed class TestDbContext : DbContext
{
    private readonly SqliteConnection _connection;

    public TestDbContext()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public DbSet<Product> Products => Set<Product>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseSqlite(_connection);

    /// <summary>
    /// Seeds <paramref name="count"/> products with sequential IDs and predictable data.
    /// Products are ordered: Id 1 → smallest price/date, Id N → largest.
    /// </summary>
    public void Seed(int count = 30)
    {
        Products.AddRange(
            Enumerable.Range(1, count).Select(i => new Product
            {
                Name      = $"Product {i:D2}",
                Price     = i * 10m,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i - 1)
            }));
        SaveChanges();
    }

    public override void Dispose()
    {
        base.Dispose();
        _connection.Dispose();
    }
}
