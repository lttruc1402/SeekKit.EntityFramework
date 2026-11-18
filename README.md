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
- 🎛️ **Client-driven sorting** — `?sort=price:desc,name:asc` against a whitelist (`SeekSortMap`), never an arbitrary column
- 🕳️ **Nullable & string sort columns** — rows holding `NULL` are paged correctly on every database
- 🔢 **Opt-in total count** and **`IAsyncEnumerable` streaming** of every page for exports and batch jobs
- 🔗 **Push-down projection** — `Select<TResult>` defers a join/projection until after the page is limited, so a join never runs against the whole table
- 🔐 **Opaque tokens** — clients never see your key values; bound to their sort, optionally HMAC-signed and expiring
- 🧩 **Pure LINQ** — no raw SQL; EF Core translates everything to provider-native queries
- 🛠️ **Extensible** — custom type converters, custom serializers, custom filter strategies
- ✅ Targets **.NET 8, 9, 10, 11** (EF Core 8/9/10/11) and **.NET Standard 2.1** (EF Core 5)

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

`Map` runs **after** the page is fetched — it reshapes rows already in memory. It's
fine for renaming/computing fields, but a join written inside it still has to hit
the database separately (or N+1) for each item.

### Push-down projection — `Select`

`ISeekBuilder<T>.Select<TResult>(transformer)` instead pushes a join/projection
**into the same query**, and defers it until *after* ordering, keyset filtering,
and the look-ahead `Take` have already limited the row set — so the join only
ever runs against the page you asked for, not the whole table, in a single
database round trip:

```csharp
SeekResult<OrderDto> page = await seek
    .CreateBuilder(db.Orders)
    .OrderByDescending(o => o.CreatedAt)
    .OrderBy(o => o.Id)                 // unique tie-breaker — always last
    .WithRequest(request)
    .Select(q => q.Select(o => new OrderDto
    {
        Id           = o.Id,
        CreatedAt    = o.CreatedAt,
        CustomerName = o.Customer.Name  // join pushed to run only on this page
    }))
    .ToSeekResultAsync();
```

`Select` returns `ISeekBuilder<T, TResult>`, whose `ToSeekResultAsync()` yields
`SeekResult<TResult>` — same tokens, same bidirectional navigation, same
metadata, just a different item shape.

**How cursor values are read back:** the next/previous tokens are built from
the projected `TResult`, not from `T`. For each sort column SeekKit:

1. reads the **same-named public property** on `TResult` when there is one
   (here `Id` and `CreatedAt`) — keep its CLR type identical to the entity's,
   a mismatch (e.g. `Id` projected as `long` when it's `int`) isn't caught for
   you;
2. otherwise **traces the value through your transformer** to find the
   property it ends up in. A renamed member (`OrderId = o.Id`), or a key
   carried through `Join` / `LeftJoin` / `GroupJoin` + `SelectMany` and any
   number of intermediate anonymous types, just works. Tracing only inspects
   the composed expression tree — the transformer is invoked once against an
   empty in-memory queryable, no query runs.

If neither finds it, `Select()` throws `InvalidOperationException` immediately,
before any query runs.

**Sorting a scalar sequence by itself:** `OrderBy(x => x)` (e.g. on an
`IQueryable<long>` of pre-filtered ids) has no property name to derive, so it
always goes through the tracing step — including through a chain of joins:

```csharp
IQueryable<long> ids = db.Orders.Where(o => o.Status == status).Select(o => o.Id);

SeekResult<OrderListItem> page = await seek.SeekAsync(ids, request,
    q => q.Join(db.Orders, id => id, o => o.Id, (id, o) => o)
          .Join(db.Warehouses, o => o.WarehouseId, w => w.Id, (o, w) => new { o, w })
          .LeftJoin(db.Channels, x => x.o.ChannelId, c => c.Id, (x, c) => new { x.o, x.w, c })
          .Select(x => new OrderListItem
          {
              OrderId       = x.o.Id,          // ← found by tracing id → o.Id → OrderId
              WarehouseCode = x.w.Code,
              ChannelCode   = x.c!.Code,
          }),
    b => b.OrderByDescending(x => x));         // no resultPropertyName needed
```

Tracing never guesses. It gives up — and `Select()` throws with an actionable
message — when the value is **computed** (`Id = o.Id + 1`), is read from the
**optional side of an outer join** (it would be `null` for unmatched rows), or
the projection isn't built from LINQ operators. For those cases name the
property yourself: `OrderByDescending(x => x, resultPropertyName: "Id")`.

| `Select` vs `Map` | Runs | Best for |
|---|---|---|
| `Select<TResult>(transformer)` | Inside the query, before materialization | Joins/projections you want pushed to the database |
| `Map(mapper)` | In memory, after materialization | Reshaping already-fetched items, no new data needed |

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

### Client-driven sorting — `SeekSortMap`

Let the client choose the sort (`?sort=price:desc,name:asc`) without letting it sort by
anything it likes. A `SeekSortMap<T>` is a whitelist: only the fields you
register can be sorted by, so nobody can force an `ORDER BY` on an unindexed or
private column.

```csharp
static readonly SeekSortMap<Product> ProductSorts = new SeekSortMap<Product>()
    .Add("createdAt", p => p.CreatedAt)
    .Add("price",     p => p.Price)
    .Add("name",      p => p.Name)
    .Add("id",        p => p.Id)
    .Default("createdAt:desc")  // used when the client sends no sort
    .TieBreaker("id");          // appended automatically — keyset paging needs a unique last column

app.MapGet("/products", async (ISeekService seek, AppDbContext db, string? sort, string? token, int? pageSize) =>
{
    try
    {
        return Results.Ok(await seek.SeekAsync(
            db.Products,
            new SeekRequest { Token = token, PageSize = pageSize },
            b => b.ApplySort(ProductSorts, sort)));
    }
    catch (SeekSortException ex)  { return Results.Problem(ex.Message, statusCode: 400); }   // unknown field, bad syntax
    catch (SeekTokenException ex) { return Results.Problem(ex.Message, statusCode: 400); }   // token from another sort, expired, …
});
```

| Sort string | Means |
|---|---|
| `price:asc` | ascending |
| `price:desc` | descending |
| `price` | ascending — the direction may be left out |
| `price:desc,name:asc` | several fields, in priority order |
| *(empty)* | the map's `Default(...)` |

That is the only spelling: `-price`, `+price` and `price desc` are rejected with a
message telling the client to write `field:asc` / `field:desc`. Field names and
directions are case-insensitive. An unknown field, a field listed twice, bad
syntax, or more than `LimitTo(n)` fields (default 5) throws `SeekSortException`
— an `ArgumentException` whose message lists the sortable fields. The
tie-breaker follows the direction of the first field unless you pin it
(`TieBreaker("id", descending: false)`), which keeps same-direction sorts
eligible for PostgreSQL's row-value comparison. `ProductSorts.Resolve(sort)`
returns the effective sort if you want to echo it back to the client.

Give every sortable field its own index — `(Price, Id)`, `(Name, Id)`, … — the
same way the default sort has one. And because a token only makes sense together
with the sort it was issued for, a client that changes `sort` while holding an old
token gets a `SeekTokenException` (see [Token validation](#token-validation-expiry-and-errors))
instead of a page from the wrong position.

### Nullable sort columns

Sorting by a column that can hold `NULL` (`int?`, `DateTime?`, `string?`) just
works, on every strategy:

```csharp
b.OrderByDescending(t => t.DueAt)   // DateTime? — rows without a due date are paged too
 .OrderBy(t => t.Id)
```

A comparison against `NULL` is never true in SQL or MongoDB, so a plain keyset
filter silently drops those rows from every page after the first. SeekKit makes
the filter null-aware instead (`DueAt < @p OR DueAt IS NULL`, `DueAt IS NOT NULL`,
…) — it never changes the `ORDER BY`, so your indexes keep working. All it needs
to know is where the database itself puts `NULL`, which follows from the
configured provider:

| Database | `NULL` in ascending order | `NullOrdering` |
|---|---|---|
| SQL Server, MySQL, SQLite, MongoDB | first | `NullsSmallest` (default) |
| PostgreSQL, Oracle | last | `NullsLargest` (default for `ForPostgreSql` / `ForOracle`) |

Override `options.NullOrdering` only if your database sorts the other way round.
Two things to know:

- A column counts as nullable when it is `Nullable<T>`, or a reference type not
  annotated as non-null. `string Name` in a nullable-enabled project is treated
  as non-null and keeps the plain comparison; `string? Name`, or any string in a
  project without nullable annotations, gets the null-aware filter.
- PostgreSQL's row-value comparison `(a, b) > (@a, @b)` can't express this, so a
  sort containing a nullable column uses the fallback strategy (`UNION ALL` by
  default) instead — just as fast, see [Measured](#measured-why-the-strategy-matters).

The tie-breaker (last) column should still be unique **and** non-null.

### Sorting by a computed expression

A sort key doesn't have to be a property — any expression the provider can
translate works:

```csharp
b.OrderBy(p => p.Name.ToLower())            // case-insensitive
 .OrderByDescending(p => p.Price * p.Quantity)
 .OrderBy(p => p.Id)
```

String sort keys (computed or not) are compared with `string.Compare`, which
providers translate to a plain `<` / `>` on the column — the database's collation
decides the order, exactly as for the `ORDER BY`. A computed key usually needs a
computed column or an expression index to stay fast.

With `Select()`, the cursor is read back from the projected item, and a computed
value can't be traced to one of its properties — project the same expression and
name it:

```csharp
b.OrderBy(p => p.Name.ToLower(), resultPropertyName: "NameLower")
 .OrderBy(p => p.Id)
 .Select(q => q.Select(p => new ProductDto { Id = p.Id, NameLower = p.Name.ToLower() }))
```

### Total count

`SeekResult<T>.TotalCount` is `null` unless you ask for it — counting is exactly
the full scan keyset pagination exists to avoid:

```csharp
var page = await seek
    .CreateBuilder(db.Orders.Where(o => o.CustomerId == customerId))
    .WithRequest(request)
    .OrderByDescending(o => o.CreatedAt).OrderBy(o => o.Id)
    .WithTotalCount(request.Token is null)   // first page only — later pages reuse the number
    .ToSeekResultAsync();

page.TotalCount;   // every row the query matches, across all pages
```

It costs one extra `COUNT(*)` round trip over the unpaged query (run before the
page, never concurrently — a `DbContext` allows one query at a time). With
`Select()`, the source query is what gets counted. When it is `null` the property
is left out of the JSON response.

### Walking every page — `ToPagesAsync` / `ToItemsAsync`

For exports, batch jobs, and re-indexing: stream the whole result without holding
it in memory and without writing the token loop yourself.

```csharp
var builder = seek.CreateBuilder(db.Orders.AsNoTracking())
    .WithRequest(new SeekRequest { PageSize = 1000 })   // batch size
    .OrderBy(o => o.Id);

await foreach (var order in builder.ToItemsAsync(ct))     // row by row
    await writer.WriteAsync(order);

await foreach (var page in builder.ToPagesAsync(ct))      // or batch by batch
    await bulkIndexer.IndexAsync(page.Items);
```

Each page is one keyset query, fetched only when the enumeration reaches it, so
the millionth batch costs the same as the first. The walk starts at the request's
token (or the first page) and follows `NextToken` to the end; `break` out of the
loop whenever you like. With `WithTotalCount()` the count runs once, before the
first page. Both methods also exist on the projected builder returned by
`Select()`.

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
the first page, or throw with `InvalidTokenBehavior.Throw` — see below):

```csharp
builder.Services.AddSeekKit(
    options => options.Strategy = DatabaseStrategy.ForSqlServer(),
    config  => config.UseHmacSigning(builder.Configuration["SeekKit:TokenKey"]!));
```

The key must be at least 16 bytes (use a 32-byte random secret) and must be the
same across all instances serving the same clients. Keep it in configuration or
a secret store, not in source code.

### Token validation, expiry, and errors

Every token carries a short fingerprint of the sort it was issued for. Using it
with a different sort — other columns, another order, a flipped direction —
throws `SeekTokenException` rather than returning rows from the wrong position.
(Tokens issued by SeekKit 2.3 and earlier carry no fingerprint and keep working.)

Tokens that can't be decoded at all are, by default, ignored: the request gets the
first page. Opt into strict handling to reject them instead, and optionally give
tokens a lifetime:

```csharp
builder.Services.AddSeekKit(options =>
{
    options.Strategy             = DatabaseStrategy.ForSqlServer();
    options.InvalidTokenBehavior = SeekInvalidTokenBehavior.Throw;   // default: FirstPage
    options.TokenLifetime        = TimeSpan.FromHours(1);            // default: null — never expires
});
```

`SeekTokenException.Error` says what was wrong. It is always a client mistake, so
map it to `400 Bad Request`:

| `Error` | Meaning | Thrown |
|---|---|---|
| `SortMismatch` | issued for a different sort than this request uses | always |
| `InvalidValue` | holds a value that isn't valid for its sort column | always |
| `Malformed` | truncated, foreign, or signature mismatch | only with `InvalidTokenBehavior.Throw` |
| `Expired` | older than `TokenLifetime` (or has no expiry while one is configured) | only with `InvalidTokenBehavior.Throw` |

The expiry is stored inside the token, so it is only tamper-proof together with
`UseHmacSigning`. Turning `TokenLifetime` on invalidates tokens issued before it.
`SeekTokenException` derives from `InvalidOperationException`.

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

| Strategy | SQL pattern | Notes |
|----------|-------------|-------|
| `Auto` *(default)* | row-value, else the fallback | Picks the row-value comparison whenever the sort allows it |
| `Tuple` | `WHERE (a, b) > (@a, @b)` | Fastest; needs every column to sort the same direction and none to be nullable |
| `UnionAll` | one `LIMIT`-ed branch per index range, `UNION ALL` | Handles mixed directions and nullable columns; the default fallback |
| `OrLogic` | OR predicates | **Avoid on PostgreSQL** — see the measurements below |

### SQL Server — `DatabaseStrategy.ForSqlServer(...)`

| Strategy | SQL pattern | Notes |
|----------|-------------|-------|
| `UnionAll` *(default)* | one `TOP N` branch per index range, `UNION ALL` | Every branch is an ordered index seek |
| `OrLogic` | OR predicates | The optimizer often answers it with a scan — see below |

### MySQL / Oracle / SQLite

| Strategy | Notes |
|----------|-------|
| `UnionAll` *(default)* | Translated to `LIMIT` (MySQL/SQLite) or `FETCH FIRST n ROWS ONLY` (Oracle) |
| `OrLogic` | Fine on MySQL; slow on Oracle for large tables — see below |

Every engine is covered by an integration suite that runs against the real
database in a container: PostgreSQL 17, SQL Server 2022, MySQL 8.4 (with Oracle's
`MySql.EntityFrameworkCore` provider), Oracle Database 23, and MongoDB 7; SQLite
in the unit tests.

### Measured: why the strategy matters

One page of 50 rows starting in the **middle** of a 2,000,000-row table, with a
composite index matching each sort (local Docker containers, warm cache — the
ratios matter, not the absolute numbers).

**With the default strategy** — a deep page costs the same as the first one:

| Sort | PostgreSQL 17 | SQL Server 2022 | MySQL 8.4 | Oracle 23 |
|---|---|---|---|---|
| two non-null columns, mixed directions | 1.7 ms | 1.7 ms | 2.0 ms | 1.6 ms |
| non-null string, then id | 1.1 ms *(row-value)* | 1.4 ms | 4.3 ms | 1.5 ms |
| nullable column ascending, then id | 1.2 ms | 1.2 ms | 1.9 ms | 1.1 ms |
| nullable column descending, then id | 1.2 ms | 1.4 ms | 5.1 ms | 1.0 ms |
| *the same page with `OFFSET`* | *0.1–2.0 s* | *0.5–0.8 s* | *1.1–1.6 s* | *0.4–3.7 s* |

**With `OrLogic` / `WithOrPredicate()`** instead:

| Sort | PostgreSQL 17 | SQL Server 2022 | MySQL 8.4 | Oracle 23 |
|---|---|---|---|---|
| two non-null columns, mixed directions | 101 ms | 394 ms | 1.8 ms | 75 ms |
| non-null string, then id | 1,701 ms | 2.0 ms | 2.2 ms | 76 ms |
| nullable column ascending, then id | 1,219 ms | 416 ms | 2.4 ms | 54 ms |
| nullable column descending, then id | 805 ms | 435 ms | 2.1 ms | 43 ms |

Take-aways:

- **Leave the defaults alone.** They are fast on all four engines, for every
  kind of sort.
- **`OrLogic` defeats the point of keyset pagination** on PostgreSQL, SQL Server
  and Oracle: their planners can't read the index in order through an `OR`, so
  they fetch and sort everything beyond the cursor — up to as slow as `OFFSET`.
  MySQL is the one engine that handles it well. Use it only for small tables or
  as a last-resort fallback.
- **MongoDB** has no such problem: its `$or` of plain field ranges is answered
  from the index (about 51 index keys examined for a 50-document page in the
  middle of a 1,000,000-document collection).
- None of this helps without an index that matches the sort — one per sortable
  combination, ending in the unique tie-breaker.

### Fallback

When the primary strategy cannot run, the fallback takes over. On PostgreSQL that
means "the row-value comparison isn't possible" (mixed directions, a nullable
column, more than 8 columns); elsewhere it means `UNION ALL` threw while being
built:

```csharp
options.Strategy = DatabaseStrategy.ForPostgreSql(
    strategy: PostgreSqlStrategy.Auto,
    fallback: FallbackStrategy.UnionAll);   // the PostgreSQL default; OrLogic | None (throw)
```

`ForPostgreSql` defaults to `FallbackStrategy.UnionAll` (it was `OrLogic` up to
2.3); the other engines default to `OrLogic`.

### Client-side overhead

SeekKit adds a few microseconds per request on top of EF Core: building the
builder and two sort columns takes ~3.5 µs and ~1.5 KB. Sort-column accessors are
compiled once per process, the keyset values are sent as query parameters rather
than literals, and `Select()` tracing — only used when the projection renames a
sort column — adds roughly 40 µs (it builds the projection's expression tree one
extra time and walks it).

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
| `InvalidTokenBehavior` | `SeekInvalidTokenBehavior` | `FirstPage` | Undecodable/expired token: ignore it, or `Throw` a `SeekTokenException` |
| `TokenLifetime` | `TimeSpan?` | `null` | How long a token stays valid; `null` = forever |
| `NullOrdering` | `SeekNullOrdering?` | `null` | Where the database sorts `NULL`; `null` = the provider's native behaviour |

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
| `TotalCount` | `long?` | Rows across all pages; `null` unless `WithTotalCount()` was used |
| `PageMetadata.PageSize` | `int` | Effective page size after clamping |
| `PageMetadata.RequestedAt` | `DateTime` | UTC timestamp of the query |
| `Map(mapper)` | `SeekResult<TDest>` | Projects items, preserving all tokens/metadata |
| `WithValue(key, value)` | `SeekResult<T>` | Attaches an extra JSON property to the response |

### `ISeekBuilder<T>`

| Method | Description |
|--------|-------------|
| `WithRequest(request)` | Sets token + page size |
| `OrderBy(expr)` / `OrderByDescending(expr)` | Adds keyset sort columns (priority = call order); a property, `x => x`, or a computed expression |
| `ApplySort(sortMap, sort)` | Adds the columns a client's sort string asks for, validated against a `SeekSortMap<T>` |
| `WithTotalCount(include = true)` | Also returns `SeekResult<T>.TotalCount` (one extra `COUNT` query) |
| `WithUnionAll()` / `WithOrPredicate()` / `WithTupleComparison()` | Pins a built-in strategy |
| `WithStrategy(ISeekFilterStrategy)` | Pins a custom strategy |
| `Select<TResult>(transformer)` | Defers a join/projection to run after the page is limited; returns `ISeekBuilder<T, TResult>` |
| `ToSeekResultAsync(ct)` | Executes and returns `SeekResult<T>` |
| `ToPagesAsync(ct)` | `IAsyncEnumerable<SeekResult<T>>` — this page and every following one, fetched lazily |
| `ToItemsAsync(ct)` | `IAsyncEnumerable<T>` — the same walk, row by row |

### `ISeekBuilder<T, TResult>`

Returned by `ISeekBuilder<T>.Select<TResult>(transformer)`. Ordering, filtering,
and strategy are already locked in from the `T`-typed builder, so this only has:

| Method | Description |
|--------|-------------|
| `ToSeekResultAsync(ct)` | Executes the projected query and returns `SeekResult<TResult>` |
| `ToPagesAsync(ct)` / `ToItemsAsync(ct)` | Walks every projected page / row as an `IAsyncEnumerable` |

---

## Requirements

| Target | EF Core | Notes |
|--------|---------|-------|
| .NET 8.0 / 9.0 / 10.0 / 11.0 | 8.x / 9.x / 10.x / 11.x | Recommended |
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
| `IQueryable<T>` | `SeekAsync<T, TResult>(query, request, transformer, configure[, configureOptions])` | + a push-down `Select` projection |
| `IQueryable<T>` | `CreateBuilder(query)` → fluent | You prefer the fluent builder |

```csharp
// one-call
var page = await seek.SeekAsync(
    db.Products.Where(p => p.IsActive), request,
    b => b.OrderByDescending(p => p.CreatedAt).OrderBy(p => p.Id));

// one-call, projected — same idea as CreateBuilder(...).Select(...), no fluent chain
var projected = await seek.SeekAsync(
    db.Products.Where(p => p.IsActive), request,
    transformer: q => q.Select(p => new ProductDto { Id = p.Id, CreatedAt = p.CreatedAt }),
    configure:   b => b.OrderByDescending(p => p.CreatedAt).OrderBy(p => p.Id));

// fluent builder
var page = await seek.CreateBuilder(db.Products)
    .WithRequest(request)
    .OrderByDescending(p => p.CreatedAt)
    .OrderBy(p => p.Id)
    .ToSeekResultAsync();
```

There are also `IQueryable<T>` extension methods (`IQueryableHelper`) if you have
an `ISeekService`, `ISeekFactory`, or `IServiceProvider` in hand instead of
building the fluent chain yourself:

| Extension | Returns |
|-----------|---------|
| `query.ToSeekResultAsync(serviceProvider\|seekFactory\|seekService, request, configure, ct)` | `SeekResult<T>` |
| `query.ToSeekResultAsync<T, TResult>(..., transformer, configure, ct)` | `SeekResult<TResult>` — one-call `Select` |
| `query.ToSeekBuilder(serviceProvider\|seekFactory\|seekService[, request])` | `ISeekBuilder<T>` |

```csharp
// One-call projected pagination without touching the fluent builder directly
SeekResult<OrderDto> page = await db.Orders.ToSeekResultAsync(
    seekFactory, request,
    transformer: q => q.Select(o => new OrderDto { Id = o.Id, CreatedAt = o.CreatedAt }),
    configure:   b => b.OrderByDescending(o => o.CreatedAt).OrderBy(o => o.Id));
```

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

// Any of the four also has a SeekAsync<T, TResult> overload taking a transformer —
// one-call projected pagination, same idea as CreateBuilder(...).Select(...):
await seek.SeekAsync(
    products.AsQueryable().Where(p => p.IsActive), request,
    transformer: q => q.Select(p => new ProductDto { Id = p.Id, CreatedAt = p.CreatedAt }),
    configure:   b => b.OrderByDescending(p => p.CreatedAt).OrderBy(p => p.Id));
```

The keyset predicate becomes an indexable `$or` filter — create a compound index
matching your sort (e.g. `{ CreatedAt: -1, _id: 1 }`). For the aggregation and
find overloads, the sort columns must be real fields on the output and the last
one globally unique.

`seek.CreateBuilder(...)` returns an origin-specific builder interface —
`ISeekMongoQueryableBuilder<T>`, `ISeekMongoAggregateBuilder<T>`, or
`ISeekMongoFindBuilder<T>` — matching the source you passed in. All three extend
`ISeekMongoBuilder<T>` (`WithRequest`/`OrderBy`/`OrderByDescending`/`ToSeekResultAsync`),
so ordinary usage doesn't need to care; the split only matters because each
origin's `Select` takes a different transformer shape (see below), and
`WithStrategy` exists **only** on the queryable builder — Mongo has no
aggregate/find-specific `ISeekFilterStrategy` to switch to, so calling it on the
other two is now a compile error instead of a runtime one.

### Sorting, counting and streaming on MongoDB

Everything from the usage guide above works on the MongoDB builders too:
`ApplySort(sortMap, sort)`, nullable sort keys (MongoDB sorts `null` and missing
fields first), string sort keys, `WithTotalCount()`, `ToPagesAsync()` /
`ToItemsAsync()`, and token validation/expiry via `AddSeekKitMongo(options => …)`.
Two origin-specific notes:

- **Computed sort keys** (`OrderBy(p => p.Name.ToLower())`) work on the
  **queryable** origin only. `$sort` and find sorts take field paths, so the
  aggregate/find builders throw `NotSupportedException` — add the value as a
  field first (`$addFields` / `$set`) and sort by that.
- **`WithTotalCount()`** counts the source as you passed it in: `LongCountAsync`
  for a queryable, a `$count` stage appended to a copy of the pipeline, or
  `CountDocumentsAsync` under the find's own filter.

### Push-down projection — `Select`

Just like the EF Core provider, every Mongo builder origin has a `Select<TResult>`
that defers a join/projection until after the keyset filter, sort, and limit —
one transformer shape per origin, matching what that origin's driver API supports:

```csharp
// Queryable/collection — Func<IQueryable<T>, IQueryable<TResult>>
SeekResult<ProductDto> page = await seek
    .CreateBuilder(products)                 // ISeekMongoQueryableBuilder<Product>
    .OrderByDescending(p => p.CreatedAt)
    .OrderBy(p => p.Id)
    .WithRequest(request)
    .Select(q => q.Select(p => new ProductDto { Id = p.Id, CreatedAt = p.CreatedAt }))
    .ToSeekResultAsync();

// Aggregation pipeline — Func<IAggregateFluent<T>, IAggregateFluent<TResult>>
// Use raw BsonDocument stages for $lookup/$project — the LINQ3 provider can't
// translate a typed Lookup<>() or BsonDocument indexer chains.
SeekResult<ProductWithCategory> page = await seek
    .CreateBuilder(products.Aggregate())     // ISeekMongoAggregateBuilder<Product>
    .OrderByDescending(p => p.CreatedAt)
    .OrderBy(p => p.Id)
    .WithRequest(request)
    .Select(pipeline => pipeline
        .AppendStage<BsonDocument>(lookupStage)      // $lookup into "categories"
        .AppendStage<ProductWithCategory>(projectStage)) // $project into the DTO
    .ToSeekResultAsync();

// Find — Func<IFindFluent<T, T>, IFindFluent<T, TResult>>
SeekResult<ProductDto> page = await seek
    .CreateBuilder(products.Find(filter))    // ISeekMongoFindBuilder<Product>
    .OrderByDescending(p => p.CreatedAt)
    .OrderBy(p => p.Id)
    .WithRequest(request)
    .Select(f => f.Project(Builders<Product>.Projection.Expression(
        p => new ProductDto { Id = p.Id, CreatedAt = p.CreatedAt })))
    .ToSeekResultAsync();
```

`Select` returns `ISeekMongoBuilder<T, TResult>` (`ToSeekResultAsync()` →
`SeekResult<TResult>`) regardless of which origin produced it. Cursor values
are read back from the projected type: for the **queryable origin** exactly as
in EF (same-named property first, otherwise traced through the LINQ
transformer — see above). The aggregate/find origins build pipelines outside
LINQ's expression trees, so nothing can be traced there: the projected type
must expose public properties with the same names/types as the sort columns
(`Id`, `CreatedAt` above), and an identity sort (`OrderBy(x => x)`) always
needs the property name passed explicitly:
`OrderByDescending(x => x, resultPropertyName: "Id")`. See
[`examples/SeekKit.Example.MongoApi`](examples/SeekKit.Example.MongoApi)'s
`GET /products/projected` for the full, runnable `$lookup` version.

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

Runnable end-to-end demos — .NET 11 minimal APIs with Docker Compose and bulk
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
