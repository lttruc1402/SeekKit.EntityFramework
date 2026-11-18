# SeekKit.Example.Api

A minimal .NET 11 web API showing [SeekKit.EntityFramework](../../README.md) cursor pagination
against SQL Server, with an offset-pagination endpoint for a side-by-side
performance comparison on a huge table.

## What's inside

| File | Purpose |
|------|---------|
| [`Program.cs`](Program.cs) | Minimal API: `/products` (seek), `/products/projected` (seek + push-down projection), `/products/offset` (offset) |
| [`docker-compose.yml`](docker-compose.yml) | SQL Server 2022 + the API |
| [`scripts/seed-products.sql`](scripts/seed-products.sql) | Creates the DB (`Products` + `Categories`) and bulk-generates rows (resumable, batched) |
| [`Dockerfile`](Dockerfile) | Multi-stage build of the API |

## Run it

### 1. Start SQL Server

```bash
cd examples/SeekKit.Example.Api
docker compose up -d sqlserver
```

### 2. Seed data

The seed script defaults to **5,000,000,000 rows** — a stress-test scale that
needs ~400 GB of disk and many hours. For a quick demo, edit `@TargetRows`
in [`scripts/seed-products.sql`](scripts/seed-products.sql) first (e.g. `5000000` = 5M rows, a couple of minutes).
The script is resumable — it continues from `MAX(Id)` if you stop and rerun it.

```bash
docker exec -it seekkit-sqlserver /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P 'SeekKit!Passw0rd' -C -i /scripts/seed-products.sql
```

### 3. Start the API

```bash
docker compose up -d --build api     # http://localhost:8080
```

Or run it on the host (uses `localhost,1433` from appsettings.json):

```bash
dotnet run --project .               # http://localhost:5080
```

## Try it

### Cursor (seek) pagination — SeekKit

```bash
# First page
curl "http://localhost:8080/products?pageSize=20"

# Next page — pass nextToken from the previous response
curl "http://localhost:8080/products?pageSize=20&token=eyJ0eXBlIjoiTmV4dCIs..."
```

Response:

```json
{
  "items": [ ... ],
  "nextToken": "eyJ0eXBlIjoiTmV4dCIs...",
  "previousToken": null,
  "hasNext": true,
  "hasPrevious": false,
  "count": 20,
  "pageMetadata": { "pageSize": 20, "requestedAt": "..." },
  "elapsedMs": 3.2
}
```

### Cursor pagination with a push-down projection — `Select`

`Products` has a `CategoryId` FK into a `Categories` table. `GET /products/projected`
returns a joined, DTO-shaped page using `ISeekBuilder<T>.Select<TResult>`:

```csharp
var result = await seek
    .CreateBuilder(db.Products.AsNoTracking())
    .OrderByDescending(p => p.CreatedAt)
    .OrderBy(p => p.Id)
    .WithRequest(new SeekRequest { Token = token, PageSize = pageSize })
    .Select(q => q.Select(p => new ProductSummary
    {
        Id = p.Id, CreatedAt = p.CreatedAt, Name = p.Name,
        CategoryName = p.Category!.Name
    }))
    .ToSeekResultAsync(ct);
```

```bash
curl "http://localhost:8080/products/projected?pageSize=20"
```

The join to `Categories` only runs against the already ordered, keyset-filtered,
and `Take`-limited page — SQL Server never joins the whole table. `ProductSummary`
exposes the sort columns under the same names/types as `Product` (`Id`, `CreatedAt`),
which is where SeekKit reads cursor values back from (a projection that renames them
is traced automatically); see
[`Data/ProductSummary.cs`](Data/ProductSummary.cs).

### Client-driven sorting — `?sort=`

`GET /products/sorted` lets the caller choose the sort, restricted to a whitelist.
Each entry of `sort` is written `field:asc` or `field:desc`:

```csharp
var productSorts = new SeekSortMap<Product>()
    .Add("createdAt", p => p.CreatedAt)
    .Add("price",     p => p.Price)
    .Add("name",      p => p.Name)
    .Add("id",        p => p.Id)
    .Default("-createdAt")
    .TieBreaker("id");          // unique last column, appended automatically

var result = await seek.SeekAsync(
    db.Products.AsNoTracking(),
    new SeekRequest { Token = token, PageSize = pageSize },
    b => b.ApplySort(productSorts, sort)
          .WithTotalCount(total == true && token is null),
    ct);
```

```bash
curl "http://localhost:8080/products/sorted"                              # default: -createdAt
curl "http://localhost:8080/products/sorted?sort=price"                   # cheapest first
curl "http://localhost:8080/products/sorted?sort=-price,name"             # priciest first, then by name
curl "http://localhost:8080/products/sorted?sort=name%20desc&total=true"  # + totalCount on the first page
```

The response echoes the sort that was applied (`"sort": "-price,name,-id"`). Bad
input is a `400` with a problem-details body, produced by the small
`SeekErrors.AsBadRequest` endpoint filter at the bottom of `Program.cs`:

```bash
curl -i "http://localhost:8080/products/sorted?sort=isActive"
# 400 — Cannot sort by 'isActive'. Sortable fields: createdAt, price, name, id.

# a token from one sort, replayed with another
curl -i "http://localhost:8080/products/sorted?sort=price:desc&token=<token from sort=price:asc>"
# 400 — The pagination token was issued for a different sort order than this request uses.
```

> **Index every sortable field.** The seed script only creates the
> `(CreatedAt DESC, Id)` index. Before sorting by `price` or `name` on the big
> table, run [`scripts/sort-indexes.sql`](scripts/sort-indexes.sql) — without a
> matching index SQL Server has to sort the whole table for each page.

### Offset pagination — for comparison

```bash
# Page 1: fast
curl "http://localhost:8080/products/offset?page=1&pageSize=20"

# Deep page: watch elapsedMs explode as SQL Server scans and discards rows
curl "http://localhost:8080/products/offset?page=10000000&pageSize=20"
```

With enough rows seeded, the deep-offset request takes **seconds to minutes**,
while following `nextToken` on `/products` stays at a **few milliseconds** no
matter how deep you go — that flat line is the whole point of SeekKit.

Both endpoints return `elapsedMs` so you can compare directly.

## How the index makes it fast

The API sorts by `CreatedAt DESC, Id ASC`, and the seed script creates the
matching composite index:

```sql
CREATE NONCLUSTERED INDEX IX_Products_CreatedAt_Id
    ON dbo.Products (CreatedAt DESC, Id ASC);
```

SeekKit turns "the next page after token X" into a keyset predicate that SQL
Server answers with a single index seek — independent of how many rows precede
the cursor position.
