# SeekKit.Example.MongoApi

A minimal .NET 10 web API showing [SeekKit.MongoDB](../../README.md#mongodb--iseekmongoservice) cursor pagination
against MongoDB, with a skip/limit endpoint for a side-by-side performance
comparison on a large collection.

## What's inside

| File | Purpose |
|------|---------|
| [`Program.cs`](Program.cs) | Minimal API: `/products` (seek), `/products/projected` (seek + push-down `$lookup`), `/products/skip` (skip/limit), `/products/union` (LINQ union), `/products/union-aggregate` (aggregation pipeline), `/products/find` (BSON find filter) |
| [`docker-compose.yml`](docker-compose.yml) | MongoDB 7 + the API |
| [`scripts/seed-products.js`](scripts/seed-products.js) | Bulk-generates documents in `products` + a `categories` lookup collection (resumable, batched) |
| [`Dockerfile`](Dockerfile) | Multi-stage build of the API |

## Run it

### 1. Start MongoDB

```bash
cd examples/SeekKit.Example.MongoApi
docker compose up -d mongo
```

### 2. Seed data

Defaults to **5,000,000 documents** (a few minutes, a few GB). Edit
`TARGET_DOCS` in [`scripts/seed-products.js`](scripts/seed-products.js) to scale up or down. The script
is resumable — rerunning continues from the current count. It also creates
the compound index `{ CreatedAt: -1, _id: 1 }` that the seek sort relies on.

```bash
docker exec -it seekkit-mongo mongosh /scripts/seed-products.js
```

### 3. Start the API

```bash
docker compose up -d --build api    # http://localhost:8081
```

Or run it on the host (uses `mongodb://localhost:27017` from appsettings.json):

```bash
dotnet run --project .              # http://localhost:5081
```

## Try it

### Cursor (seek) pagination — SeekKit

```bash
# First page
curl "http://localhost:8081/products?pageSize=20"

# Next page — pass nextToken from the previous response
curl "http://localhost:8081/products?pageSize=20&token=eyJ0eXBlIjoiTmV4dCIs..."
```

The response shape is identical to the EF Core example — same
`SeekResult<T>`, same token format — that's the point of the shared
SeekKit.Core contracts.

### Cursor pagination with a push-down projection — `Select`

Each product has a `CategoryId` pointing into a `categories` collection.
`GET /products/projected` returns a joined, DTO-shaped page using
`ISeekMongoAggregateBuilder<T>.Select<TResult>`:

```csharp
var result = await seek
    .CreateBuilder(products.Aggregate())
    .OrderByDescending(p => p.CreatedAt)
    .OrderBy(p => p.Id)
    .WithRequest(new SeekRequest { Token = token, PageSize = pageSize })
    .Select(pipeline => pipeline
        .AppendStage<BsonDocument>(lookupStage)     // $lookup into "categories"
        .AppendStage<ProductSummary>(projectStage)) // $project into the DTO shape
    .ToSeekResultAsync(ct);
```

```bash
curl "http://localhost:8081/products/projected?pageSize=20"
```

The `$lookup`/`$project` stages only run against the already keyset-filtered,
sorted, and `$limit`-ed page — Mongo never looks up categories for the whole
collection. They're appended as raw `BsonDocument` pipeline stages rather than
a typed `Lookup<>()`/LINQ `.Project()`, because the LINQ3 provider can't
translate `BsonDocument` indexer chains; see [`Program.cs`](Program.cs) for the
full stage definitions. `ProductSummary` must expose the same sort-column
names/types as `Product` (`Id`, `CreatedAt`) so SeekKit can read cursor values
from the projected shape; see [`Data/ProductSummary.cs`](Data/ProductSummary.cs).

### Skip/limit — for comparison

```bash
# Page 1: fast
curl "http://localhost:8081/products/skip?page=1&pageSize=20"

# Deep page: watch elapsedMs grow as Mongo walks and discards documents
curl "http://localhost:8081/products/skip?page=100000&pageSize=20"
```

Both endpoints return `elapsedMs` so you can compare directly. With a few
million documents, deep `skip` takes seconds while following `nextToken`
stays at a few milliseconds.

### Paginate across two collections (`$unionWith`)

`GET /products/union` pages the `products` and `products_archive` collections
as a single ordered stream:

```csharp
var union = live.AsQueryable().Union(archive.AsQueryable());   // → $unionWith
var page  = await seek.SeekAsync(union, request,
    b => b.OrderByDescending(p => p.CreatedAt).OrderBy(p => p.Id));
```

```bash
curl "http://localhost:8081/products/union?pageSize=20"
```

This was verified end-to-end against a live MongoDB: two collections merged,
paged forward and backward, no gaps or duplicates. A few things to know:

- **Use LINQ `.Union()`** — the driver translates it to a `$unionWith` stage
  and SeekKit's keyset `$or` filter works on top of it.
- **Do not use `.Concat()`** (SQL `UNION ALL`). The MongoDB LINQ provider
  currently throws on `Concat` combined with `OrderBy` + `Take`.
- The **tie-breaker column (`_id`) must be globally unique across both
  collections**, otherwise the cursor position is ambiguous.
- The merged stream is sorted after the union, so create the compound index on
  **both** collections (the example does this at startup).

### Paginate an aggregation pipeline (`$lookup`, custom stages)

`GET /products/union-aggregate` does the same thing via the aggregation-fluent
API. Use this overload when the pipeline can't be expressed in LINQ:

```csharp
IAggregateFluent<Product> pipeline = live.Aggregate().UnionWith(archive);
var page = await seek.SeekAsync(pipeline, request,
    b => b.OrderByDescending(p => p.CreatedAt).OrderBy(p => p.Id));
```

SeekKit appends a keyset `$match`, a `$sort`, and a `$limit` to whatever
pipeline you pass — so `$lookup`, `$graphLookup`, `$unionWith`, and custom
`$match` stages all work. Verified end-to-end against a live MongoDB (forward,
backward, and a pre-`$match` filter). The same tie-breaker rule applies: sort
columns must be real fields on the pipeline output, and the last one globally
unique.

### Paginate a `Find(bsonFilter)` query

`GET /products/find` paginates a find query built from a BSON
`FilterDefinition` — use this when the filter can't be written in LINQ (text
search, geo, `$expr`, ...):

```csharp
var filter = Builders<Product>.Filter.Eq(p => p.IsActive, true);
IFindFluent<Product, Product> find = products.Find(filter);
var page = await seek.SeekAsync(find, request,
    b => b.OrderByDescending(p => p.CreatedAt).OrderBy(p => p.Id));
```

SeekKit AND-s the keyset predicate into your filter, so the base filter is
preserved on every page. Verified end-to-end against a live MongoDB.

## How the index makes it fast

The API sorts by `CreatedAt DESC, _id ASC`. SeekKit turns "the next page
after token X" into an `$or` keyset filter which the MongoDB query planner
answers with per-branch index scans on:

```js
db.products.createIndex({ CreatedAt: -1, _id: 1 })
```

Check it yourself: append `.explain()` to the equivalent query in `mongosh`
and look for `IXSCAN` stages — no `COLLSCAN`, no large `docsExamined`.
