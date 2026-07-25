# SeekKit.MongoDB

High-performance **cursor (keyset) pagination** for MongoDB.
Constant-time page navigation on any collection size, with opaque bidirectional
tokens — built on the official MongoDB .NET driver.

Shares its token format and result contracts (`SeekResult<T>`, `SeekRequest`)
with [SeekKit.EntityFramework](https://www.nuget.org/packages/SeekKit.EntityFramework),
so SQL and MongoDB endpoints can expose one identical pagination API.

## Quick start

```csharp
// 1. Register (Program.cs)
builder.Services.AddSeekKitMongo(options =>
{
    options.DefaultPageSize = 20;
    options.MaxPageSize     = 100;
});

// 2. Paginate a collection
public class ProductService(ISeekMongoService seek, IMongoCollection<Product> products)
{
    public async Task<SeekResult<Product>> GetPageAsync(SeekRequest request)
        => await seek.SeekAsync(
            products.AsQueryable().Where(p => p.IsActive),
            request,
            b => b.OrderByDescending(p => p.CreatedAt)
                  .OrderBy(p => p.Id));   // unique column last (e.g. _id)
}
```

The response carries opaque `nextToken` / `previousToken` values — the client
passes them back to navigate. The keyset predicate is translated into an
indexable `$or` filter; create a compound index matching your sort
(e.g. `{ CreatedAt: -1, _id: 1 }`) and page 1,000,000 costs the same as page 1.

## Entry points

Every query model of the driver is supported — same `(source, request, configure)`
call, same `SeekResult<T>`:

| Source | Use when |
|--------|----------|
| `IMongoCollection<T>` | Paginate a whole collection |
| `IQueryable<T>` | LINQ query — `AsQueryable().Where(...)`, `.Union(...)` across collections |
| `IAggregateFluent<T>` | Aggregation pipeline — `$unionWith`, `$lookup`, custom stages |
| `IFindFluent<T, T>` | BSON `FilterDefinition` — text/geo/`$expr` filters LINQ can't express |

```csharp
// aggregation pipeline
await seek.SeekAsync(live.Aggregate().UnionWith(archive), request,
    b => b.OrderByDescending(p => p.CreatedAt).OrderBy(p => p.Id));

// find with a BSON filter
await seek.SeekAsync(products.Find(Builders<Product>.Filter.Text("wireless")), request,
    b => b.OrderByDescending(p => p.Score).OrderBy(p => p.Id));
```

`seek.CreateBuilder(...)` returns an origin-specific builder —
`ISeekMongoQueryableBuilder<T>` / `ISeekMongoAggregateBuilder<T>` /
`ISeekMongoFindBuilder<T>` — but all three share `WithRequest`/`OrderBy`/
`OrderByDescending`/`ToSeekResultAsync`, so ordinary usage doesn't need to care
which one you got.

## Push-down projection

Every builder origin has a `Select<TResult>` that defers a join/projection to
run after the keyset filter, sort, and limit — one transformer shape per origin:

```csharp
// Queryable/collection
await seek.CreateBuilder(products)
    .OrderByDescending(p => p.CreatedAt).OrderBy(p => p.Id)
    .WithRequest(request)
    .Select(q => q.Select(p => new ProductDto { Id = p.Id, CreatedAt = p.CreatedAt }))
    .ToSeekResultAsync();

// Aggregation pipeline — append raw BsonDocument $lookup/$project stages
// (the LINQ3 provider can't translate a typed Lookup<>() here)
await seek.CreateBuilder(products.Aggregate())
    .OrderByDescending(p => p.CreatedAt).OrderBy(p => p.Id)
    .WithRequest(request)
    .Select(pipeline => pipeline
        .AppendStage<BsonDocument>(lookupStage)
        .AppendStage<ProductWithCategory>(projectStage))
    .ToSeekResultAsync();
```

`TResult` must expose public properties with the same names/types as the sort
columns so SeekKit can read cursor values from the projected shape. `WithStrategy`
exists only on the queryable builder — Mongo has no aggregate/find-specific
`ISeekFilterStrategy` to switch to.

## Features

- Constant-time paging — no `skip`, purely index-driven
- Bidirectional navigation with opaque tokens
- Paginate collections, LINQ queryables, aggregation pipelines, and find queries
- Push-down projection via `Select<TResult>` on every builder origin
- `ObjectId` supported out of the box as sort/tie-breaker column
- Multi-column sorting with mixed directions
- Optional HMAC-SHA256 token signing (`config.UseHmacSigning(key)`)
- DTO projection via `result.Map(...)` preserving all metadata (in-memory, post-fetch)
- Targets .NET 8 / 9 / 10 and .NET Standard 2.1

## Links

- **Documentation & examples**: https://github.com/lttruc1402/SeekKit.EntityFramework
- **Changelog**: https://github.com/lttruc1402/SeekKit.EntityFramework/blob/master/CHANGELOG.md
- **Report issues**: https://github.com/lttruc1402/SeekKit.EntityFramework/issues

Licensed under the [MIT License](https://github.com/lttruc1402/SeekKit.EntityFramework/blob/master/LICENSE).
