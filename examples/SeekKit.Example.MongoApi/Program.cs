using System.Diagnostics;
using MongoDB.Bson;
using MongoDB.Driver;
using SeekKit.Core;
using SeekKit.Core.Models;
using SeekKit.Example.MongoApi.Data;
using SeekKit.MongoDB;
using SeekKit.MongoDB.Builders;
using SeekKit.MongoDB.Core;

var builder = WebApplication.CreateBuilder(args);

var mongoConnection = builder.Configuration.GetConnectionString("Mongo") ?? "mongodb://localhost:27017";

builder.Services.AddSingleton<IMongoClient>(new MongoClient(mongoConnection));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase("seekkit_demo"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<Product>("products"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<Category>("categories"));

builder.Services.AddSeekKitMongo(options =>
{
    options.DefaultPageSize = 20;
    options.MaxPageSize     = 200;
});

builder.Services.AddOpenApi();

var app = builder.Build();
app.MapOpenApi();

// Compound index matching the seek sort — this is what makes keyset pagination
// O(page size). Idempotent, so it's safe to run on every startup.
// Created on both the live and the archive collection (used by /products/union).
{
    var db        = app.Services.GetRequiredService<IMongoDatabase>();
    var indexKeys = Builders<Product>.IndexKeys
        .Descending(p => p.CreatedAt)
        .Ascending(p => p.Id);
    foreach (var name in new[] { "products", "products_archive" })
        await db.GetCollection<Product>(name).Indexes
            .CreateOneAsync(new CreateIndexModel<Product>(indexKeys));
}

// ── Cursor (seek) pagination — constant time at any depth ────────────────────
// First page:  GET /products
// Next pages:  GET /products?token=<nextToken from previous response>
app.MapGet("/products", async (
    ISeekMongoService seek,
    IMongoCollection<Product> products,
    string? token,
    int? pageSize,
    CancellationToken ct) =>
{
    var sw = Stopwatch.StartNew();

    SeekResult<Product> result = await seek.SeekAsync(
        products.AsQueryable().Where(p => p.IsActive),
        new SeekRequest { Token = token, PageSize = pageSize },
        b => b.OrderByDescending(p => p.CreatedAt)
              .OrderBy(p => p.Id),          // unique tie-breaker (_id) — always last
        ct);

    return Results.Ok(result.WithValue("elapsedMs", sw.Elapsed.TotalMilliseconds));
});

// ── Cursor pagination with a push-down projection (Select) ──────────────────
// Demonstrates ISeekMongoAggregateBuilder<T>.Select<TResult>: the $lookup into
// "categories" only runs against the already keyset-filtered/sorted/$limit-ed
// page, not the whole collection. The $lookup/$project stages are appended as
// raw BsonDocument pipeline stages — the LINQ3 provider can't translate the
// BsonDocument shape a typed Lookup<> would produce, so this mirrors the same
// technique SeekKit's own integration tests use for $lookup scenarios.
app.MapGet("/products/projected", async (
    ISeekMongoService seek,
    IMongoCollection<Product> products,
    string? token,
    int? pageSize,
    CancellationToken ct) =>
{
    var sw = Stopwatch.StartNew();

    var lookupStage = new BsonDocument("$lookup", new BsonDocument
    {
        { "from", "categories" },
        { "localField", "CategoryId" },
        { "foreignField", "_id" },
        { "as", "CategoryDocs" }
    });

    var projectStage = new BsonDocument("$project", new BsonDocument
    {
        { "_id", 1 },
        { "CreatedAt", 1 },
        { "Name", 1 },
        { "CategoryName", new BsonDocument("$arrayElemAt", new BsonArray { "$CategoryDocs.Name", 0 }) }
    });

    SeekResult<ProductSummary> result = await seek
        .CreateBuilder(products.Aggregate())
        .OrderByDescending(p => p.CreatedAt)
        .OrderBy(p => p.Id)               // unique tie-breaker — always last
        .WithRequest(new SeekRequest { Token = token, PageSize = pageSize })
        .Select(pipeline => pipeline
            .AppendStage<BsonDocument>(lookupStage)
            .AppendStage<ProductSummary>(projectStage))
        .ToSeekResultAsync(ct);

    return Results.Ok(result.WithValue("elapsedMs", sw.Elapsed.TotalMilliseconds));
});

// ── Skip/limit pagination — for comparison only ──────────────────────────────
// Try a deep page (e.g. /products/skip?page=100000) and compare elapsedMs with
// the seek endpoint: skip degrades linearly, seek stays flat.
app.MapGet("/products/skip", async (
    IMongoCollection<Product> products,
    int? page,
    int? pageSize,
    CancellationToken ct) =>
{
    var p  = Math.Max(page ?? 1, 1);
    var ps = Math.Clamp(pageSize ?? 20, 1, 200);
    var sw = Stopwatch.StartNew();

    var items = await products
        .Find(x => x.IsActive)
        .SortByDescending(x => x.CreatedAt)
        .ThenBy(x => x.Id)
        .Skip((p - 1) * ps)
        .Limit(ps)
        .ToListAsync(ct);

    return Results.Ok(new
    {
        page      = p,
        pageSize  = ps,
        count     = items.Count,
        elapsedMs = sw.Elapsed.TotalMilliseconds,
        items,
    });
});

// ── Client-driven sorting — ?sort=price:desc,name:asc ───────────────────────
// The client picks the sort, but only from this whitelist: an unknown field is a
// 400, never a $sort on an arbitrary (unindexed, private) field.
// Each entry is field:asc or field:desc (the direction defaults to asc):
//   GET /products/sorted                                → default sort (createdAt:desc)
//   GET /products/sorted?sort=price:asc                 → cheapest first
//   GET /products/sorted?sort=price:desc,name:asc       → priciest first, then by name
//   GET /products/sorted?sort=name:desc&total=true      → also returns totalCount
// "id" (_id) is appended automatically as the unique tie-breaker. Each sortable
// field needs its own compound index, e.g. { price: 1, _id: 1 } — see the README.
var productSorts = new SeekSortMap<Product>()
    .Add("createdAt", p => p.CreatedAt)
    .Add("price",     p => p.Price)
    .Add("name",      p => p.Name)
    .Add("id",        p => p.Id)
    .Default("createdAt:desc")
    .TieBreaker("id");

app.MapGet("/products/sorted", async (
    ISeekMongoService seek,
    IMongoCollection<Product> products,
    string? sort,
    string? token,
    int? pageSize,
    bool? total,
    CancellationToken ct) =>
{
    var sw = Stopwatch.StartNew();

    SeekResult<Product> result = await seek
        .CreateBuilder(products.AsQueryable().Where(p => p.IsActive))
        .WithRequest(new SeekRequest { Token = token, PageSize = pageSize })
        .ApplySort(productSorts, sort)
        // Counting every matching document is the expensive part — only when asked
        // for, and only on the first page.
        .WithTotalCount(total == true && token is null)
        .ToSeekResultAsync(ct);

    return Results.Ok(result
        .WithValue("sort", string.Join(",", productSorts.Resolve(sort)))   // the sort actually applied
        .WithValue("elapsedMs", sw.Elapsed.TotalMilliseconds));
})
.AddEndpointFilter(SeekErrors.AsBadRequest);

// ── Cursor pagination over a UNION of two collections (LINQ) ─────────────────
// Pages the "products" collection together with an "products_archive" collection
// as a single stream, ordered globally. Use LINQ .Union() — the MongoDB driver
// translates it to a $unionWith stage, and SeekKit's keyset $or filter works on
// top of it. Notes:
//   • .Union() de-duplicates (→ $unionWith). .Concat() (no dedup) also works on
//     MongoDB.Driver 3.14+; older drivers threw on Concat + OrderBy + Take.
//   • For pipelines the LINQ provider can't express ($lookup, custom stages),
//     use the aggregation-fluent overload — see /products/union-aggregate below.
//   • The unique tie-breaker (_id) must be globally unique across BOTH
//     collections, otherwise the cursor position is ambiguous.
app.MapGet("/products/union", async (
    ISeekMongoService seek,
    IMongoDatabase db,
    string? token,
    int? pageSize,
    CancellationToken ct) =>
{
    var live    = db.GetCollection<Product>("products");
    var archive = db.GetCollection<Product>("products_archive");

    var union = live.AsQueryable()
        .Union(archive.AsQueryable());   // → $unionWith, one ordered stream

    var sw = Stopwatch.StartNew();

    SeekResult<Product> result = await seek.SeekAsync(
        union,
        new SeekRequest { Token = token, PageSize = pageSize },
        b => b.OrderByDescending(p => p.CreatedAt)
              .OrderBy(p => p.Id),        // globally-unique tie-breaker — always last
        ct);

    return Results.Ok(result.WithValue("elapsedMs", sw.Elapsed.TotalMilliseconds));
});

// ── Cursor pagination over an aggregation pipeline ($unionWith) ───────────────
// Same result as /products/union, but built with the aggregation-fluent API.
// Use this overload when the pipeline can't be expressed in LINQ — e.g. it has
// $lookup, $graphLookup, or custom stages. SeekKit appends a keyset $match,
// a $sort, and a $limit to whatever pipeline you pass.
app.MapGet("/products/union-aggregate", async (
    ISeekMongoService seek,
    IMongoDatabase db,
    string? token,
    int? pageSize,
    CancellationToken ct) =>
{
    var live    = db.GetCollection<Product>("products");
    var archive = db.GetCollection<Product>("products_archive");

    IAggregateFluent<Product> pipeline = live.Aggregate().UnionWith(archive);

    var sw = Stopwatch.StartNew();

    SeekResult<Product> result = await seek.SeekAsync(
        pipeline,
        new SeekRequest { Token = token, PageSize = pageSize },
        b => b.OrderByDescending(p => p.CreatedAt)
              .OrderBy(p => p.Id),
        ct);

    return Results.Ok(result.WithValue("elapsedMs", sw.Elapsed.TotalMilliseconds));
});

// ── Cursor pagination over a Find(bsonFilter) query ──────────────────────────
// Use this overload when your filter is a BSON FilterDefinition the LINQ
// provider can't express (text search, geo, $expr, ...). SeekKit AND-s the
// keyset predicate into your filter and appends a sort and a limit.
app.MapGet("/products/find", async (
    ISeekMongoService seek,
    IMongoCollection<Product> products,
    string? token,
    int? pageSize,
    CancellationToken ct) =>
{
    // Any BSON filter — here a simple range, but this is where a $text / $geoNear
    // / $expr filter would go.
    var filter = Builders<Product>.Filter.Eq(p => p.IsActive, true);
    IFindFluent<Product, Product> find = products.Find(filter);

    var sw = Stopwatch.StartNew();

    SeekResult<Product> result = await seek.SeekAsync(
        find,
        new SeekRequest { Token = token, PageSize = pageSize },
        b => b.OrderByDescending(p => p.CreatedAt)
              .OrderBy(p => p.Id),
        ct);

    return Results.Ok(result.WithValue("elapsedMs", sw.Elapsed.TotalMilliseconds));
});

app.MapGet("/", () => Results.Redirect("/products"));

app.Run();

/// <summary>
/// Turns the two exceptions that mean "the request is wrong" into 400 responses: a sort
/// string naming an unknown field, and a pagination token that can't be used (issued for
/// another sort, expired, malformed).
/// </summary>
internal static class SeekErrors
{
    public static async ValueTask<object?> AsBadRequest(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try
        {
            return await next(context);
        }
        catch (SeekSortException ex)
        {
            return Results.Problem(title: "Invalid sort", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (SeekTokenException ex)
        {
            return Results.Problem(title: "Invalid pagination token", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
    }
}
