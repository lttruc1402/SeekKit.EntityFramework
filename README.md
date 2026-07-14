<p align="center">
  <img src="icon.png" alt="SeekKit logo" width="110" />
</p>

<h1 align="center">SeekKit.EntityFramework</h1>

<p align="center">
  High-performance cursor (keyset) pagination for Entity Framework Core.<br/>
  Constant-time page navigation on any table size — SQL Server, PostgreSQL, MySQL, Oracle, and SQLite.
</p>

<p align="center">
  <a href="https://github.com/lttruc1402/SeekKit.EntityFramework/actions/workflows/ci.yml"><img src="https://github.com/lttruc1402/SeekKit.EntityFramework/actions/workflows/ci.yml/badge.svg" alt="CI" /></a>
  <a href="https://www.nuget.org/packages/SeekKit.EntityFramework"><img src="https://img.shields.io/nuget/v/SeekKit.EntityFramework.svg" alt="NuGet" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-yellow.svg" alt="License: MIT" /></a>
</p>

---

## Packages

| Package | Description |
|---------|-------------|
| [`SeekKit.EntityFramework`](https://www.nuget.org/packages/SeekKit.EntityFramework) | Cursor pagination for EF Core — SQL Server, PostgreSQL, MySQL, Oracle, SQLite |
| [`SeekKit.MongoDB`](https://www.nuget.org/packages/SeekKit.MongoDB) | Cursor pagination for MongoDB via the official driver's LINQ provider |
| [`SeekKit.Core`](https://www.nuget.org/packages/SeekKit.Core) | Shared contracts, token serialization, converters (installed automatically) |

All providers share the same `SeekResult<T>` / `SeekRequest` contracts and token
format, so SQL and MongoDB endpoints expose one identical pagination API.

> Upgrading from 1.x? See the **[2.0 migration guide](docs/MIGRATION-2.0.md)** —
> it's a 5-minute find-and-replace of `using` directives.

## What is SeekKit?

SeekKit replaces slow `Skip/Take` (offset) pagination with **keyset pagination** (also called *cursor* or *seek* pagination). Instead of asking the database to count and discard rows, it remembers *where the last page ended* and continues from that exact position:

```sql
-- Offset: scans and throws away 100,000 rows to get page 10,001
SELECT * FROM Products ORDER BY Id OFFSET 100000 ROWS FETCH NEXT 10 ROWS ONLY;

-- Keyset (SeekKit): jumps straight to the position via the index
SELECT TOP 10 * FROM Products WHERE Id > @lastSeenId ORDER BY Id;
```

The client only sees an **opaque token** — no leaking of internal keys, no fragile page numbers:

```
GET /api/products                        → first page + nextToken
GET /api/products?token=eyJ0eXBlIjo...   → next page
```

### Why cursor pagination?

| | Offset (`Skip/Take`) | Cursor (SeekKit) |
|---|---|---|
| Performance on large tables | Degrades linearly with page depth | **Constant — always fast** |
| Consistency under concurrent writes | May skip or duplicate rows | **Stable** |
| Works with infinite scroll / feeds / exports | Awkward | **Natural fit** |
| Random access ("jump to page 57") | Yes | No — sequential only |

Use SeekKit for infinite scroll, activity feeds, data exports, sync APIs, admin tables — anywhere users walk through data page by page.

### Features

- ⚡ **Constant-time paging** — no `OFFSET`, purely index-driven seeks
- 🔀 **Bidirectional** — navigate forward *and* backward with `NextToken` / `PreviousToken`
- 🗄️ **Database-tuned strategies** — tuple comparison, `UNION ALL`, or OR-predicates, chosen per engine
- 📇 **Multi-column sorting** — mixed ascending/descending directions supported
- 🔐 **Opaque tokens** — clients never see your key values
- 🧩 **Pure LINQ** — no raw SQL; EF Core translates everything to provider-native queries
- 🛠️ **Extensible** — custom type converters, custom serializers, custom filter strategies
- ✅ Targets **.NET 8, 9, 10** (EF Core 8/9/10) and **.NET Standard 2.1** (EF Core 5)

---

## Installation

```bash
dotnet add package SeekKit.EntityFramework
```

You also need the EF Core provider for your database (e.g. `Microsoft.EntityFrameworkCore.SqlServer`).

## Quick start

### 1. Register the service

```csharp
// Program.cs
using SeekKit.Core.Models;            // SeekRequest, SeekResult<T>
using SeekKit.EntityFramework;
using SeekKit.EntityFramework.Core;   // DatabaseStrategy

builder.Services.AddSeekKit(options =>
{
    // Pick the strategy matching your database:
    // ForSqlServer() | ForPostgreSql() | ForMySql() | ForOracle() | ForSqlite()
    options.Strategy        = DatabaseStrategy.ForSqlServer();
    options.DefaultPageSize = 20;
    options.MaxPageSize     = 100;
});
```

### 2. Paginate a query

Inject `ISeekService` and paginate any `IQueryable<T>`:

```csharp
public class ProductService(ISeekService seek, AppDbContext db)
{
    public async Task<SeekResult<Product>> GetPageAsync(SeekRequest request)
    {
        return await seek.SeekAsync(
            db.Products.Where(p => p.IsActive),   // any filtered query
            request,
            b => b.OrderByDescending(p => p.CreatedAt)
                  .OrderBy(p => p.Id));           // unique column LAST — required
    }
}
```

> **Rule:** the last `OrderBy` column must be **unique** (typically the primary key). This guarantees the cursor always points to an exact row, so pages never skip or duplicate items.

### 3. Expose it from a controller

```csharp
[HttpGet]
public Task<SeekResult<Product>> Get([FromQuery] string? token, [FromQuery] int? pageSize)
    => _service.GetPageAsync(new SeekRequest { Token = token, PageSize = pageSize });
```

### 4. Response shape

```json
{
  "items": [ /* 20 products */ ],
  "nextToken": "eyJ0eXBlIjoiTmV4dCIsInZhbHVlcy...",
  "previousToken": null,
  "hasNext": true,
  "hasPrevious": false,
  "count": 20,
  "pageMetadata": { "pageSize": 20, "requestedAt": "2026-07-06T10:00:00Z" }
}
```

The client passes `nextToken` back as `token` to get the next page — that's the whole protocol.

---

## Usage guide

### Fluent builder

For more control, build the query step by step with `CreateBuilder`:

```csharp
SeekResult<Product> result = await seek
    .CreateBuilder(db.Products)
    .WithRequest(request)
    .OrderByDescending(p => p.CreatedAt)
    .OrderBy(p => p.Id)
    .ToSeekResultAsync(cancellationToken);
```

### Multi-column sort (mixed directions)

Column order determines sort priority. Mixed directions are fully supported:

```csharp
b => b.OrderBy(p => p.Status)              // 1st: status ascending
      .OrderByDescending(p => p.Priority)  // 2nd: priority descending
      .OrderBy(p => p.Id)                  // 3rd: unique tie-breaker
```

### Map entities to DTOs

`Map` projects items while preserving all tokens and metadata:

```csharp
SeekResult<Product> page = await seek.SeekAsync(db.Products, request,
    b => b.OrderBy(p => p.Id));

SeekResult<ProductDto> dto = page.Map(p => new ProductDto
{
    Id    = p.Id,
    Name  = p.Name,
    Price = p.Price
});
```

### Bidirectional navigation

Every page carries tokens in both directions:

```
Page 1            Page 2            Page 3
[items] ──next──▶ [items] ──next──▶ [items]
        ◀──prev──         ◀──prev──
```

```csharp
// Forward
var page2 = await GetPageAsync(new SeekRequest { Token = page1.NextToken });

// Backward
var page1Again = await GetPageAsync(new SeekRequest { Token = page2.PreviousToken });
```

- First page: `PreviousToken == null`, `HasPrevious == false`
- Last page: `NextToken == null`, `HasNext == false`

### Override options per request

Global options stay untouched; the override applies to this call only:

```csharp
await seek.SeekAsync(
    query, request,
    b => b.OrderBy(p => p.Id),
    options => options.Strategy = DatabaseStrategy.ForPostgreSql(PostgreSqlStrategy.Tuple));
```

### Force a specific query strategy

The builder can pin a strategy regardless of configuration:

```csharp
await seek.CreateBuilder(db.Products)
    .WithRequest(request)
    .OrderBy(p => p.Id)
    .WithUnionAll()          // or .WithOrPredicate() / .WithTupleComparison()
    .ToSeekResultAsync();
```

### Attach extra data to the response

```csharp
var result = await seek.SeekAsync(db.Products, request, b => b.OrderBy(p => p.Id));
result.WithValue("totalActive", await db.Products.CountAsync(p => p.IsActive));
// serialized as an extra top-level JSON property
```

### Custom type converter

Keyset values are serialized to strings inside the token. Built-in converters cover all primitives, `Guid`, `DateTime`, `DateTimeOffset`, `DateOnly`, `TimeOnly`, `TimeSpan`, and their nullable variants. For custom value types, register a converter:

```csharp
builder.Services.AddSeekKit(
    options => options.Strategy = DatabaseStrategy.ForPostgreSql(),
    config  => config.AddConverter<Money>(
        toString:   m => m.ToInvariantString(),
        fromString: s => Money.Parse(s)));
```

Or implement `TypeConverter<T>` and pass an instance to `config.AddConverter(...)`.

### Signed tokens (tamper-proof)

By default tokens are opaque but **not signed** — a client could craft one by hand.
Enable HMAC-SHA256 signing to reject tampered or forged tokens (they fall back to
the first page):

```csharp
builder.Services.AddSeekKit(
    options => options.Strategy = DatabaseStrategy.ForSqlServer(),
    config  => config.UseHmacSigning(builder.Configuration["SeekKit:TokenKey"]!));
```

The key must be at least 16 bytes (use a 32-byte random secret) and must be the
same across all instances serving the same clients. Keep it in configuration or
a secret store, not in source code.

### Custom token serializer

Replace the default JSON+Base64 token format (e.g. to add encryption):

```csharp
builder.Services.AddSeekKit(
    options => options.Strategy = DatabaseStrategy.ForSqlServer(),
    config  => config.UseSeekSerializer<MyEncryptedSerializer>());  // implements ISeekSerializer
```

### Custom filter strategy

For full control over how the seek predicate is built, implement `ISeekFilterStrategy` and pin it on the builder:

```csharp
await seek.CreateBuilder(db.Products)
    .WithRequest(request)
    .OrderBy(p => p.Id)
    .WithStrategy(new MyFilterStrategy())
    .ToSeekResultAsync();
```

---

## Query strategies

SeekKit builds pure LINQ; your EF Core provider translates it to native SQL. Each engine gets a tuned default, configurable via `DatabaseStrategy.ForXxx(strategy, fallback)`.

### PostgreSQL — `DatabaseStrategy.ForPostgreSql(...)`

| Strategy | SQL pattern | Speed* | Notes |
|----------|-------------|--------|-------|
| `Auto` *(default)* | Tuple or fallback | ~0.6–0.9 ms | Picks the best automatically |
| `Tuple` | `WHERE (a, b) > (@a, @b)` | ~0.6 ms | Fastest; requires all columns to sort the same direction |
| `UnionAll` | `UNION ALL` + `LIMIT` | ~0.9 ms | Handles mixed sort directions |
| `OrLogic` | OR predicates | ~2 ms | Most compatible |

### SQL Server — `DatabaseStrategy.ForSqlServer(...)`

| Strategy | SQL pattern | Speed* |
|----------|-------------|--------|
| `UnionAll` *(default)* | `UNION ALL` + `TOP N` | ~5–8 ms |
| `OrLogic` | OR predicates | ~10–15 ms |

### MySQL / Oracle / SQLite

| Strategy | Notes |
|----------|-------|
| `UnionAll` *(default)* | Translated to `LIMIT` (MySQL/SQLite) or `FETCH FIRST n ROWS ONLY` (Oracle) |
| `OrLogic` | Most compatible fallback |

<sub>*Indicative timings measured on a mid-size table with a proper composite index; your numbers will vary.</sub>

### Fallback

When the primary strategy cannot run (e.g. `Tuple` with mixed sort directions), the fallback takes over:

```csharp
options.Strategy = DatabaseStrategy.ForPostgreSql(
    strategy: PostgreSqlStrategy.Tuple,
    fallback: FallbackStrategy.UnionAll);   // OrLogic (default) | UnionAll | None (throw)
```

---

## API reference

### `AddSeekKit(configureOptions, configure?)`

Registers `ISeekService` and friends as singletons.

### `SeekKitOptions`

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `Strategy` | `DatabaseStrategy` | `ForSqlServer()` | Database engine + query strategy |
| `DefaultPageSize` | `int` | `10` | Page size when the client sends none |
| `MinPageSize` | `int` | `1` | Lower clamp for `PageSize` |
| `MaxPageSize` | `int` | `1000` | Upper clamp for `PageSize` |

### `SeekRequest`

| Property | Type | Description |
|----------|------|-------------|
| `Token` | `string?` | Token from a previous result. `null` = first page |
| `PageSize` | `int?` | Items per page. `null` = `DefaultPageSize`; clamped to `[Min, Max]` |

### `SeekResult<T>`

| Member | Type | Description |
|--------|------|-------------|
| `Items` | `IReadOnlyList<T>` | Items on the current page |
| `NextToken` | `string?` | Token for the next page; `null` when at the end |
| `PreviousToken` | `string?` | Token for the previous page; `null` on the first page |
| `HasNext` / `HasPrevious` | `bool` | Whether adjacent pages exist |
| `Count` | `int` | Number of items returned |
| `PageMetadata.PageSize` | `int` | Effective page size after clamping |
| `PageMetadata.RequestedAt` | `DateTime` | UTC timestamp of the query |
| `Map(mapper)` | `SeekResult<TDest>` | Projects items, preserving all tokens/metadata |
| `WithValue(key, value)` | `SeekResult<T>` | Attaches an extra JSON property to the response |

### `ISeekBuilder<T>`

| Method | Description |
|--------|-------------|
| `WithRequest(request)` | Sets token + page size |
| `OrderBy(expr)` / `OrderByDescending(expr)` | Adds keyset sort columns (priority = call order) |
| `WithUnionAll()` / `WithOrPredicate()` / `WithTupleComparison()` | Pins a built-in strategy |
| `WithStrategy(ISeekFilterStrategy)` | Pins a custom strategy |
| `ToSeekResultAsync(ct)` | Executes and returns `SeekResult<T>` |

---

## Requirements

| Target | EF Core | Notes |
|--------|---------|-------|
| .NET 8.0 / 9.0 / 10.0 | 8.x / 9.x / 10.x | Recommended |
| .NET Standard 2.1 | 5.x | For .NET Core 3.x and .NET 5–7 apps |

- An EF Core provider for your database:

| Database | NuGet package |
|----------|---------------|
| SQL Server | `Microsoft.EntityFrameworkCore.SqlServer` |
| PostgreSQL | `Npgsql.EntityFrameworkCore.PostgreSQL` |
| MySQL / MariaDB | `Pomelo.EntityFrameworkCore.MySql` |
| Oracle | `Oracle.EntityFrameworkCore` |
| SQLite | `Microsoft.EntityFrameworkCore.Sqlite` |

## Entry points

Every `SeekAsync` overload takes the same `(source, request, configure)` shape,
returns the same `SeekResult<T>`, and produces the same token format — pick the
one that matches how you built your query.

### Entity Framework Core — `ISeekService`

| Source | Overload | Use when |
|--------|----------|----------|
| `IQueryable<T>` | `SeekAsync(query, request, configure)` | Any EF Core `DbSet`/LINQ query |
| `IQueryable<T>` | `SeekAsync(query, request, configure, configureOptions)` | + per-request option overrides |
| `IQueryable<T>` | `CreateBuilder(query)` → fluent | You prefer the fluent builder |

```csharp
// one-call
var page = await seek.SeekAsync(
    db.Products.Where(p => p.IsActive), request,
    b => b.OrderByDescending(p => p.CreatedAt).OrderBy(p => p.Id));

// fluent builder
var page = await seek.CreateBuilder(db.Products)
    .WithRequest(request)
    .OrderByDescending(p => p.CreatedAt)
    .OrderBy(p => p.Id)
    .ToSeekResultAsync();
```

There are also `IQueryable<T>` extension methods (`ToSeekResultAsync(...)`) if you
have an `ISeekService`, `ISeekFactory`, or `IServiceProvider` in hand.

### MongoDB — `ISeekMongoService`

[`SeekKit.MongoDB`](https://www.nuget.org/packages/SeekKit.MongoDB) covers every
query model of the official driver. `ObjectId` works as a sort/tie-breaker column
out of the box.

| Source | Overload | Use when |
|--------|----------|----------|
| `IMongoCollection<T>` | `SeekAsync(collection, ...)` | Paginate a whole collection |
| `IQueryable<T>` | `SeekAsync(queryable, ...)` | LINQ query — `AsQueryable().Where(...)`, `.Union(...)` |
| `IAggregateFluent<T>` | `SeekAsync(aggregate, ...)` | Aggregation pipeline — `$unionWith`, `$lookup`, custom stages |
| `IFindFluent<T, T>` | `SeekAsync(find, ...)` | BSON `FilterDefinition` — text/geo/`$expr` filters LINQ can't express |

```csharp
builder.Services.AddSeekKitMongo(options => options.DefaultPageSize = 20);

// 1. Whole collection
await seek.SeekAsync(products, request,
    b => b.OrderByDescending(p => p.CreatedAt).OrderBy(p => p.Id));

// 2. LINQ queryable (filters, .Union across collections)
await seek.SeekAsync(
    products.AsQueryable().Where(p => p.IsActive), request,
    b => b.OrderByDescending(p => p.CreatedAt).OrderBy(p => p.Id));

// 3. Aggregation pipeline — SeekKit appends a keyset $match, $sort, $limit
IAggregateFluent<Product> pipeline = live.Aggregate().UnionWith(archive);
await seek.SeekAsync(pipeline, request,
    b => b.OrderByDescending(p => p.CreatedAt).OrderBy(p => p.Id));

// 4. Find with a BSON filter — SeekKit AND-s the keyset predicate into it
IFindFluent<Product, Product> find = products.Find(Builders<Product>.Filter.Text("wireless"));
await seek.SeekAsync(find, request,
    b => b.OrderByDescending(p => p.Score).OrderBy(p => p.Id));
```

The keyset predicate becomes an indexable `$or` filter — create a compound index
matching your sort (e.g. `{ CreatedAt: -1, _id: 1 }`). For the aggregation and
find overloads, the sort columns must be real fields on the output and the last
one globally unique.

### Both providers in one app

Register both — they share one converter registry and one token serializer,
in any order:

```csharp
builder.Services.AddSeekKit(o => o.Strategy = DatabaseStrategy.ForSqlServer());
builder.Services.AddSeekKitMongo(o => o.DefaultPageSize = 20);
// inject ISeekService for EF queries, ISeekMongoService for Mongo collections
```

Custom converters and `UseHmacSigning` registered through either provider's
`configure` apply to both, so tokens stay consistent app-wide.

## Example projects

Runnable end-to-end demos — .NET 10 minimal APIs with Docker Compose and bulk
seed scripts for benchmarking seek vs offset yourself:

| Example | Database | |
|---------|----------|---|
| [`examples/SeekKit.Example.Api`](examples/SeekKit.Example.Api) | SQL Server 2022 | seek vs `OFFSET/FETCH` |
| [`examples/SeekKit.Example.MongoApi`](examples/SeekKit.Example.MongoApi) | MongoDB 7 | seek vs `skip/limit` |

```bash
cd examples/SeekKit.Example.Api       # or SeekKit.Example.MongoApi
docker compose up -d                  # database + API
# then seed data — see each example's README
```

## Contributing

Issues and pull requests are welcome! Run the test suite before submitting:

```bash
dotnet test SeekKit.EntityFramework.slnx
```

## License

This project is licensed under the [MIT License](LICENSE).
