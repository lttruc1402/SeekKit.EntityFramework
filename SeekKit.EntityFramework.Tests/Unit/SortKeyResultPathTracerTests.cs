using System.Linq.Expressions;
using SeekKit.Core.Helpers;

namespace SeekKit.EntityFramework.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SortKeyResultPathTracer{T, TResult}"/> — the static trace that
/// finds which projected property holds a sort column's value. Pure expression-tree work:
/// every queryable here is an empty in-memory sequence and nothing is ever enumerated.
/// </summary>
public sealed class SortKeyResultPathTracerTests
{
    private sealed class Order
    {
        public long     Id          { get; set; }
        public string   Code        { get; set; } = "";
        public int      WarehouseId { get; set; }
        public int?     ChannelId   { get; set; }
        public DateTime CreatedAt   { get; set; }
    }

    private sealed class Warehouse
    {
        public int    WarehouseId { get; set; }
        public string Code        { get; set; } = "";
    }

    private sealed class Channel
    {
        public int    ChannelId { get; set; }
        public string Code      { get; set; } = "";
    }

    private sealed class OrderDto
    {
        public long      OrderId       { get; set; }
        public string    Code          { get; set; } = "";
        public string    WarehouseCode { get; set; } = "";
        public string?   ChannelCode   { get; set; }
        public DateTime  Created       { get; set; }
    }

    private sealed record OrderRecord(long Id, string Code);

    private static readonly IQueryable<Order>     Orders     = Array.Empty<Order>().AsQueryable();
    private static readonly IQueryable<Warehouse> Warehouses = Array.Empty<Warehouse>().AsQueryable();
    private static readonly IQueryable<Channel>   Channels   = Array.Empty<Channel>().AsQueryable();

    private static string? TraceIdentity<TResult>(Func<IQueryable<long>, IQueryable<TResult>> transformer)
        => Trace(transformer, x => x);

    private static string? Trace<T, TKey, TResult>(
        Func<IQueryable<T>, IQueryable<TResult>> transformer,
        Expression<Func<T, TKey>> keySelector)
        => new SortKeyResultPathTracer<T, TResult>(transformer).TryTrace(keySelector);

    // ── Identity selector (x => x) ────────────────────────────────────────────

    [Fact]
    public void Identity_ThroughJoinChainAndLeftJoin_FindsProjectedMember()
    {
        // ids → Join (keeps the entity) → Join (wraps in an anonymous pair) → LeftJoin
        // (re-wraps) → Select into a DTO. The key lands on a member named unlike its source.
        var path = TraceIdentity(q => q
            .Join(Orders, id => id, o => o.Id, (id, o) => o)
            .Join(Warehouses, o => o.WarehouseId, w => w.WarehouseId, (o, w) => new { o, w })
            .LeftJoin(Channels, x => x.o.ChannelId, c => c.ChannelId, (x, c) => new { x.o, x.w, c })
            .Select(x => new OrderDto
            {
                OrderId       = x.o.Id,
                Code          = x.o.Code,
                WarehouseCode = x.w.Code,
                ChannelCode   = x.c!.Code,
            }));

        Assert.Equal(nameof(OrderDto.OrderId), path);
    }

    [Fact]
    public void Identity_ThroughGroupJoinSelectManyLeftJoin_FindsProjectedMember()
    {
        // The pre-.NET 10 way to write a left join.
        var path = TraceIdentity(q => q
            .Join(Orders, id => id, o => o.Id, (id, o) => o)
            .GroupJoin(Channels, o => o.ChannelId, c => c.ChannelId, (o, channels) => new { o, channels })
            .SelectMany(x => x.channels.DefaultIfEmpty(), (x, c) => new OrderDto
            {
                OrderId     = x.o.Id,
                ChannelCode = c!.Code,
            }));

        Assert.Equal(nameof(OrderDto.OrderId), path);
    }

    [Fact]
    public void Identity_PassesThroughFilteringAndOrderingOperators()
    {
        var path = TraceIdentity(q => q
            .Where(id => id > 0)
            .Join(Orders, id => id, o => o.Id, (id, o) => o)
            .Where(o => o.Code != "")
            .OrderBy(o => o.Code)
            .Take(5)
            .Select(o => new OrderDto { OrderId = o.Id }));

        Assert.Equal(nameof(OrderDto.OrderId), path);
    }

    [Fact]
    public void Identity_KeptFromOuterSideOfJoin_FindsProjectedMember()
    {
        // The identity value itself (not the inner key) is what gets projected.
        var path = TraceIdentity(q => q
            .Join(Orders, id => id, o => o.Id, (id, o) => new { id, o })
            .Select(x => new OrderDto { OrderId = x.id, Code = x.o.Code }));

        Assert.Equal(nameof(OrderDto.OrderId), path);
    }

    [Fact]
    public void Identity_WhenIdsAreTheInnerSideOfJoin_FindsProjectedMember()
    {
        var path = TraceIdentity(q => Orders
            .Join(q, o => o.Id, id => id, (o, id) => new OrderDto { OrderId = o.Id }));

        Assert.Equal(nameof(OrderDto.OrderId), path);
    }

    [Fact]
    public void Identity_CompositeJoinKey_MatchesKeyPartsByPosition()
    {
        var path = TraceIdentity(q => q
            .Join(Orders,
                id => new { Id = id, Warehouse = 1 },
                o  => new { o.Id, Warehouse = o.WarehouseId },
                (id, o) => new OrderDto { OrderId = o.Id }));

        Assert.Equal(nameof(OrderDto.OrderId), path);
    }

    [Fact]
    public void Identity_PositionalRecord_MatchesConstructorParameterToProperty()
    {
        var path = TraceIdentity(q => q
            .Join(Orders, id => id, o => o.Id, (id, o) => new OrderRecord(o.Id, o.Code)));

        Assert.Equal(nameof(OrderRecord.Id), path);
    }

    [Fact]
    public void Identity_NestedProjection_ReturnsDottedPath()
    {
        var path = TraceIdentity(q => q.Select(id => new { Info = new { Key = id }, Label = "x" }));

        Assert.Equal("Info.Key", path);
    }

    [Fact]
    public void Identity_WidenedToNullable_StillTraced()
    {
        var path = TraceIdentity(q => q.Select(id => new { Key = (long?)id }));

        Assert.Equal("Key", path);
    }

    [Fact]
    public void Identity_ProjectedItemIsTheValueItself_ReturnsSelf()
    {
        var path = TraceIdentity(q => q.Where(id => id > 0));

        Assert.Equal(SortKeyPathTracing.Self, path);
    }

    // ── Must not guess ────────────────────────────────────────────────────────

    [Fact]
    public void Identity_ComputedValue_IsNotTraced()
    {
        var path = TraceIdentity(q => q
            .Join(Orders, id => id, o => o.Id, (id, o) => new OrderDto { OrderId = o.Id + 1000 }));

        Assert.Null(path);
    }

    [Fact]
    public void Identity_ReadFromOptionalSideOfLeftJoin_IsNotTraced()
    {
        // ids are the optional side: unmatched orders carry no id, so it can't be a cursor.
        var path = TraceIdentity(q => Orders
            .LeftJoin(q, o => o.Id, id => id, (o, id) => new { Key = id }));

        Assert.Null(path);
    }

    [Fact]
    public void Identity_InnerKeyOfLeftJoin_IsNotTraced()
    {
        // c.ChannelId only equals the id for matched rows; for the rest it is null.
        var path = TraceIdentity(q => q
            .LeftJoin(Channels, id => (int?)id, c => (int?)c.ChannelId, (id, c) => new { Key = c!.ChannelId }));

        Assert.Null(path);
    }

    [Fact]
    public void Identity_IndexedSelect_IsNotTraced()
    {
        // Same element type in and out, but the values are rewritten.
        var path = TraceIdentity(q => q.Select((id, index) => id + index));

        Assert.Null(path);
    }

    [Fact]
    public void TransformerThatThrowsOnTheProbe_IsNotTraced()
    {
        var path = TraceIdentity<OrderDto>(_ => throw new InvalidOperationException("needs a real provider"));

        Assert.Null(path);
    }

    // ── Ordinary member selectors (x => x.Member) ─────────────────────────────

    [Fact]
    public void Member_RenamedByProjection_FindsNewName()
    {
        var path = Trace<Order, DateTime, OrderDto>(
            q => q.Select(o => new OrderDto { OrderId = o.Id, Created = o.CreatedAt }),
            o => o.CreatedAt);

        Assert.Equal(nameof(OrderDto.Created), path);
    }

    [Fact]
    public void Member_CarriedThroughJoin_FindsProjectedMember()
    {
        var path = Trace<Order, long, OrderDto>(
            q => q
                .Join(Warehouses, o => o.WarehouseId, w => w.WarehouseId, (o, w) => new { o, w })
                .Select(x => new OrderDto { OrderId = x.o.Id, WarehouseCode = x.w.Code }),
            o => o.Id);

        Assert.Equal(nameof(OrderDto.OrderId), path);
    }

    [Fact]
    public void Member_ThatIsTheJoinKey_IsFoundOnTheInnerSide()
    {
        // Sorting by o.WarehouseId while the projection only exposes w.WarehouseId —
        // equal on every row of an inner join.
        var path = Trace(
            (IQueryable<Order> q) => q.Join(Warehouses, o => o.WarehouseId, w => w.WarehouseId,
                (o, w) => new { Warehouse = w.WarehouseId, o.Code }),
            (Order o) => o.WarehouseId);

        Assert.Equal("Warehouse", path);
    }

    [Fact]
    public void Member_DroppedByProjection_IsNotTraced()
    {
        var path = Trace<Order, DateTime, OrderDto>(
            q => q.Select(o => new OrderDto { OrderId = o.Id }),
            o => o.CreatedAt);

        Assert.Null(path);
    }
}
