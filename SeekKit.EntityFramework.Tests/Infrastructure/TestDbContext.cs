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
    public DbSet<Ticket>   Tickets    => Set<Ticket>();

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

    /// <summary>
    /// Seeds <paramref name="count"/> tickets whose nullable columns are NULL on a regular
    /// pattern and whose non-null values repeat, so every sort hits both ties and NULLs.
    /// </summary>
    public void SeedTickets(int count = 40)
    {
        var day = new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        Tickets.AddRange(
            Enumerable.Range(1, count).Select(i => new Ticket
            {
                Title    = $"Title {(i * 7) % 11:D2}",
                Assignee = i % 3 == 0 ? null : $"user{i % 6}",
                Priority = i % 5 == 0 ? null : i % 4,
                DueAt    = i % 4 == 0 ? null : day.AddDays(i % 7),
            }));
        SaveChanges();
    }

    public override void Dispose()
    {
        base.Dispose();
        _connection.Dispose();
    }
}
