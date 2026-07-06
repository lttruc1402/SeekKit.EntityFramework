using Microsoft.EntityFrameworkCore;

namespace SeekKit.Example.Api.Data;

public class ShopDbContext(DbContextOptions<ShopDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // The table and indexes are created by scripts/seed-products.sql —
        // this mapping only has to match its schema.
        modelBuilder.Entity<Product>(e =>
        {
            e.ToTable("Products");
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).HasMaxLength(64);
            e.Property(p => p.Price).HasPrecision(18, 2);
        });
    }
}
