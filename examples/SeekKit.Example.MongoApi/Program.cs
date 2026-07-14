using System.Diagnostics;
using MongoDB.Driver;
using SeekKit.Core.Models;
using SeekKit.Example.MongoApi.Data;
using SeekKit.MongoDB;
using SeekKit.MongoDB.Core;

var builder = WebApplication.CreateBuilder(args);

var mongoConnection = builder.Configuration.GetConnectionString("Mongo") ?? "mongodb://localhost:27017";

builder.Services.AddSingleton<IMongoClient>(new MongoClient(mongoConnection));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase("seekkit_demo"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IMongoDatabase>().GetCollection<Product>("products"));

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

// ── Cursor pagination over a UNION of two collections (LINQ) ─────────────────
// Pages the "products" collection together with an "products_archive" collection
// as a single stream, ordered globally. Use LINQ .Union() — the MongoDB driver
// translates it to a $unionWith stage, and SeekKit's keyset $or filter works on
// top of it. Notes:
//   • Use .Union() (dedup, → $unionWith), NOT .Concat() — the LINQ provider
//     currently throws on Concat + OrderBy + Take.
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
