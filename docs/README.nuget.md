# SeekKit.EntityFramework

High-performance **cursor (keyset) pagination** for Entity Framework Core.
Constant-time page navigation on any table size — SQL Server, PostgreSQL, MySQL, Oracle, and SQLite.

## Why cursor pagination?

`Skip/Take` (offset) pagination gets slower the deeper users page, because the
database scans and discards every preceding row. SeekKit remembers *where the
last page ended* and continues from that exact position with an index seek —
page 1 and page 1,000,000 cost the same.

| | Offset (`Skip/Take`) | Cursor (SeekKit) |
|---|---|---|
| Performance on large tables | Degrades with page depth | **Constant — always fast** |
| Consistency under writes | May skip/duplicate rows | **Stable** |
| Infinite scroll / feeds / exports | Awkward | **Natural fit** |

## Quick start

```csharp
// 1. Register (Program.cs)
builder.Services.AddSeekKit(options =>
{
    options.Strategy        = DatabaseStrategy.ForSqlServer(); // or ForPostgreSql(), ForMySql(), ForOracle(), ForSqlite()
    options.DefaultPageSize = 20;
});

// 2. Paginate any IQueryable
public class ProductService(ISeekService seek, AppDbContext db)
{
    public async Task<SeekResult<Product>> GetPageAsync(SeekRequest request)
        => await seek.SeekAsync(
            db.Products.Where(p => p.IsActive),
            request,
            b => b.OrderByDescending(p => p.CreatedAt)
                  .OrderBy(p => p.Id));   // unique column last
}
```

The response carries opaque `nextToken` / `previousToken` values — the client
just passes them back to navigate. Bidirectional, URL-safe, no leaked keys.

## Push-down projection

`ISeekBuilder<T>.Select<TResult>(transformer)` defers a join/projection until
*after* the keyset filter, sort, and look-ahead `Take` have already limited the
row set — so a join to another table only ever runs against the page you asked
for, in one database round trip:

```csharp
SeekResult<OrderDto> page = await seek
    .CreateBuilder(db.Orders)
    .OrderByDescending(o => o.CreatedAt)
    .OrderBy(o => o.Id)
    .WithRequest(request)
    .Select(q => q.Select(o => new OrderDto
    {
        Id = o.Id, CreatedAt = o.CreatedAt, CustomerName = o.Customer.Name
    }))
    .ToSeekResultAsync();
```

`TResult` must expose public properties with the same names/types as the sort
columns (`Id`, `CreatedAt` here) so SeekKit can read cursor values from the
projected shape.

## Features

- Constant-time paging — no `OFFSET`, purely index-driven seeks
- Bidirectional navigation with opaque tokens
- Database-tuned strategies: tuple comparison, `UNION ALL`, OR-predicates
- Multi-column sorting with mixed directions
- Push-down projection via `Select<TResult>` — joins run only on the limited page
- Optional HMAC-SHA256 token signing (`config.UseHmacSigning(key)`)
- DTO projection via `result.Map(...)` preserving all metadata (in-memory, post-fetch)
- Extensible: custom type converters, serializers, filter strategies
- Targets .NET 8 / 9 / 10 (EF Core 8/9/10) and .NET Standard 2.1 (EF Core 5)

## Links

- **Documentation & examples**: https://github.com/lttruc1402/SeekKit.EntityFramework
- **Changelog**: https://github.com/lttruc1402/SeekKit.EntityFramework/blob/master/CHANGELOG.md
- **Report issues**: https://github.com/lttruc1402/SeekKit.EntityFramework/issues

Licensed under the [MIT License](https://github.com/lttruc1402/SeekKit.EntityFramework/blob/master/LICENSE).
