using MongoDB.Driver;
using SeekKit.Core;
using SeekKit.Core.Strategies;
using Xunit.Abstractions;

namespace SeekKit.MongoDB.Tests;

/// <summary>
/// Guards the <em>shape</em> of the keyset filter the MongoDB LINQ provider produces. A
/// keyset filter is only fast when it is made of plain field comparisons
/// (<c>{ Name: { $gt: … } }</c>) the planner can turn into index bounds; the same logic
/// expressed through <c>$expr</c> is correct but forces a collection scan. Rendering a
/// query needs no server, so this runs everywhere.
/// </summary>
public sealed class KeysetFilterTranslationTests
{
    private sealed class Doc
    {
        public ObjectId Id { get; set; }
        public string Name { get; set; } = "";
        public string? Owner { get; set; }
        public int? Score { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private readonly ITestOutputHelper _output;
    private readonly ISeekValueConverter _converter;
    private readonly IMongoCollection<Doc> _collection;

    public KeysetFilterTranslationTests(ITestOutputHelper output)
    {
        _output = output;

        var services = new ServiceCollection();
        services.AddSeekKitMongo();
        _converter = services.BuildServiceProvider().GetRequiredService<ISeekValueConverter>();

        // Creating a collection handle does not connect.
        _collection = new MongoClient("mongodb://localhost:27017").GetDatabase("test").GetCollection<Doc>("docs");
    }

    private string Render(ISortColumn<Doc>[] columns, SeekDirection direction, Dictionary<string, string> values)
    {
        var predicate = KeysetPredicateBuilder.BuildOrPredicate(
            columns, new SeekData { Direction = direction, Values = values }, _converter)!;

        var rendered = _collection.AsQueryable().Where(predicate).ToString()!;
        _output.WriteLine(rendered);
        return rendered;
    }

    private static readonly string AnId = ObjectId.GenerateNewId().ToString();

    [Fact]
    public void StringSortKey_BecomesAPlainRangeFilter()
    {
        var rendered = Render(
            [new SortColumn<Doc, string>("Name", d => d.Name, isDescending: false), new SortColumn<Doc, ObjectId>("Id", d => d.Id, isDescending: false)],
            SeekDirection.Next,
            new() { ["Name"] = "M", ["Id"] = AnId });

        Assert.DoesNotContain("$expr", rendered);
        Assert.Contains("\"Name\" : { \"$gt\" : \"M\" }", rendered);
    }

    [Fact]
    public void StringSortKey_Descending_BecomesAPlainRangeFilter()
    {
        var rendered = Render(
            [new SortColumn<Doc, string>("Name", d => d.Name, isDescending: true), new SortColumn<Doc, ObjectId>("Id", d => d.Id, isDescending: false)],
            SeekDirection.Next,
            new() { ["Name"] = "M", ["Id"] = AnId });

        Assert.DoesNotContain("$expr", rendered);
        Assert.Contains("\"Name\" : { \"$lt\" : \"M\" }", rendered);
    }

    [Fact]
    public void NullableSortKeys_StayPlainFieldFilters()
    {
        // Descending over nulls-smallest: the NULLs lie ahead, so the filter gains a
        // { Score: null } branch — still a plain, indexable field match.
        var rendered = Render(
            [new SortColumn<Doc, int?>("Score", d => d.Score, isDescending: true), new SortColumn<Doc, string?>("Owner", d => d.Owner, isDescending: false), new SortColumn<Doc, ObjectId>("Id", d => d.Id, isDescending: false)],
            SeekDirection.Next,
            new() { ["Score"] = "5", ["Owner"] = "ann", ["Id"] = AnId });

        Assert.DoesNotContain("$expr", rendered);
        Assert.Contains("\"Score\" : null", rendered);
    }

    [Fact]
    public void NullBoundary_StaysAPlainFieldFilter()
    {
        var rendered = Render(
            [new SortColumn<Doc, int?>("Score", d => d.Score, isDescending: false), new SortColumn<Doc, ObjectId>("Id", d => d.Id, isDescending: false)],
            SeekDirection.Next,
            new() { ["Score"] = null!, ["Id"] = AnId });

        Assert.DoesNotContain("$expr", rendered);
        Assert.Contains("\"$ne\" : null", rendered);
    }
}
