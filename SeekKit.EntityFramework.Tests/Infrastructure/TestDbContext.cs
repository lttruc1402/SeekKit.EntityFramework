namespace SeekKit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// EF Core DbContext backed by a SQLite in-memory database.
/// The <see cref="SqliteConnection"/> is kept open for the lifetime of this context
/// so the in-memory database survives across multiple SaveChanges / query calls.
/// </summary>
public sealed class TestDbContext : DbContext
{
    private readonly SqliteConnection _connection;
    private readonly IInterceptor[] _interceptors;

    public TestDbContext(params IInterceptor[] interceptors)
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _interceptors = interceptors;
    }

    public DbSet<Product>  Products   => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite(_connection);
        if (_interceptors.Length > 0)
            optionsBuilder.AddInterceptors(_interceptors);
    }

    /// <summary>
    /// Seeds 3 categories and <paramref name="count"/> products with sequential IDs and
    /// predictable data, cycling products across the 3 categories.
    /// Products are ordered: Id 1 → smallest price/date, Id N → largest.
    /// </summary>
    public void Seed(int count = 30)
    {
        var categories = new List<Category>
        {
            new() { Name = "Electronics" },
            new() { Name = "Groceries" },
            new() { Name = "Books" },
        };
        Categories.AddRange(categories);
        SaveChanges();

        Products.AddRange(
            Enumerable.Range(1, count).Select(i => new Product
            {
                Name       = $"Product {i:D2}",
                Price      = i * 10m,
                CreatedAt  = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i - 1),
                CategoryId = categories[(i - 1) % categories.Count].Id
            }));
        SaveChanges();
    }

    public override void Dispose()
    {
        base.Dispose();
        _connection.Dispose();
    }
}
