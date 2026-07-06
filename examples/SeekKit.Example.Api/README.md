# SeekKit.Example.Api

A minimal .NET 10 web API showing [SeekKit.EntityFramework](../../README.md) cursor pagination
against SQL Server, with an offset-pagination endpoint for a side-by-side
performance comparison on a huge table.

## What's inside

| File | Purpose |
|------|---------|
| [`Program.cs`](Program.cs) | Minimal API: `/products` (seek) and `/products/offset` (offset) |
| [`docker-compose.yml`](docker-compose.yml) | SQL Server 2022 + the API |
| [`scripts/seed-products.sql`](scripts/seed-products.sql) | Creates the DB and bulk-generates rows (resumable, batched) |
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
