using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SeekKit.Core;
using SeekKit.Core.Models;
using SeekKit.EntityFramework;
using SeekKit.EntityFramework.Builders;
using SeekKit.EntityFramework.Core;
using SeekKit.Example.Api.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ShopDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddSeekKit(options =>
{
    options.Strategy        = DatabaseStrategy.ForSqlServer();
    options.DefaultPageSize = 20;
    options.MaxPageSize     = 200;
});

builder.Services.AddOpenApi();

var app = builder.Build();
app.MapOpenApi();

// ── Cursor (seek) pagination — constant time at any depth ────────────────────
// First page:  GET /products
// Next pages:  GET /products?token=<nextToken from previous response>
app.MapGet("/products", async (
    ISeekService seek,
    ShopDbContext db,
    string? token,
    int? pageSize,
    CancellationToken ct) =>
{
    var sw = Stopwatch.StartNew();

    SeekResult<Product> result = await seek.SeekAsync(
        db.Products.AsNoTracking(),
        new SeekRequest { Token = token, PageSize = pageSize },
        b => b.OrderByDescending(p => p.CreatedAt)
              .OrderBy(p => p.Id),          // unique tie-breaker — always last
        ct);

    return Results.Ok(result.WithValue("elapsedMs", sw.Elapsed.TotalMilliseconds));
});

// ── Cursor pagination with a push-down projection (Select) ──────────────────
// Demonstrates ISeekBuilder<T>.Select<TResult>: the join to Categories only
// runs against the already ordered/filtered/limited page, not the whole table.
app.MapGet("/products/projected", async (
    ISeekService seek,
    ShopDbContext db,
    string? token,
    int? pageSize,
    CancellationToken ct) =>
{
    var sw = Stopwatch.StartNew();

    var result = await seek
        .CreateBuilder(db.Products.AsNoTracking())
        .OrderByDescending(p => p.CreatedAt)
        .OrderBy(p => p.Id)               // unique tie-breaker — always last
        .WithRequest(new SeekRequest { Token = token, PageSize = pageSize })
        .Select(q => q.Select(p => new ProductSummary
        {
            Id           = p.Id,
            CreatedAt    = p.CreatedAt,
            Name         = p.Name,
            CategoryName = p.Category!.Name
        }))
        .ToSeekResultAsync(ct);

    return Results.Ok(result.WithValue("elapsedMs", sw.Elapsed.TotalMilliseconds));
});

// ── Offset pagination — for comparison only ──────────────────────────────────
// Try a deep page (e.g. /products/offset?page=1000000) and compare elapsedMs
// with the seek endpoint: offset degrades linearly, seek stays flat.
app.MapGet("/products/offset", async (
    ShopDbContext db,
    int? page,
    int? pageSize,
    CancellationToken ct) =>
{
    var p  = Math.Max(page ?? 1, 1);
    var ps = Math.Clamp(pageSize ?? 20, 1, 200);
    var sw = Stopwatch.StartNew();

    var items = await db.Products.AsNoTracking()
        .OrderByDescending(x => x.CreatedAt)
        .ThenBy(x => x.Id)
        .Skip((p - 1) * ps)
        .Take(ps)
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

app.MapGet("/", () => Results.Redirect("/products"));

app.Run();
