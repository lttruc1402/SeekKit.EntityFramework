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

// ── Client-driven sorting — ?sort=price:desc,name:asc ───────────────────────
// The client picks the sort, but only from this whitelist: an unknown field is a
// 400, never an ORDER BY on an arbitrary (unindexed, private) column.
// Each entry is field:asc or field:desc (the direction defaults to asc):
//   GET /products/sorted                                → default sort (createdAt:desc)
//   GET /products/sorted?sort=price:asc                 → cheapest first
//   GET /products/sorted?sort=price:desc,name:asc       → priciest first, then by name
//   GET /products/sorted?sort=name:desc&total=true      → also returns totalCount
// "id" is appended automatically as the unique tie-breaker. Each sortable field
// needs its own index — see scripts/sort-indexes.sql.
var productSorts = new SeekSortMap<Product>()
    .Add("createdAt", p => p.CreatedAt)
    .Add("price",     p => p.Price)
    .Add("name",      p => p.Name)
    .Add("id",        p => p.Id)
    .Default("createdAt:desc")
    .TieBreaker("id");

app.MapGet("/products/sorted", async (
    ISeekService seek,
    ShopDbContext db,
    string? sort,
    string? token,
    int? pageSize,
    bool? total,
    CancellationToken ct) =>
{
    var sw = Stopwatch.StartNew();

    SeekResult<Product> result = await seek.SeekAsync(
        db.Products.AsNoTracking(),
        new SeekRequest { Token = token, PageSize = pageSize },
        b => b.ApplySort(productSorts, sort)
              // COUNT(*) over the whole table is the expensive part — only when asked
              // for, and only on the first page.
              .WithTotalCount(total == true && token is null),
        ct);

    return Results.Ok(result
        .WithValue("sort", string.Join(",", productSorts.Resolve(sort)))   // the sort actually applied
        .WithValue("elapsedMs", sw.Elapsed.TotalMilliseconds));
})
.AddEndpointFilter(SeekErrors.AsBadRequest);

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
