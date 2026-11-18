using System.Globalization;

namespace SeekKit.EntityFramework.Tests.Unit;

/// <summary>
/// Exhaustive in-memory simulation of the null-aware keyset predicate for both null
/// orderings a database can have. SQLite (the integration tests' database) only covers
/// "nulls smallest"; this is where the PostgreSQL/Oracle side ("nulls largest") is
/// pinned down — the rows are sorted here exactly as such a database would sort them, and
/// the predicate has to page through that order without skipping or repeating a row.
/// </summary>
public sealed class NullAwareKeysetPredicateTests
{
    private sealed record Row(int Id, int? Key, int? Second);

    private static readonly ISeekValueConverter Converter = BuildConverter();

    private static ISeekValueConverter BuildConverter()
    {
        var services = new ServiceCollection();
        services.AddSeekKitCore();
        return services.BuildServiceProvider().GetRequiredService<ISeekValueConverter>();
    }

    // 24 rows; Key and Second are NULL on different rows and repeat, Id is the unique tie-breaker.
    private static readonly Row[] Rows = Enumerable.Range(1, 24)
        .Select(i => new Row(
            Id:     i,
            Key:    i % 4 == 0 ? null : i % 3,
            Second: i % 5 == 0 ? null : i % 2))
        .ToArray();

    public static TheoryData<bool, bool, SeekNullOrdering, int> Cases()
    {
        var data = new TheoryData<bool, bool, SeekNullOrdering, int>();
        foreach (var keyDescending in new[] { false, true })
        foreach (var secondDescending in new[] { false, true })
        foreach (var ordering in new[] { SeekNullOrdering.NullsSmallest, SeekNullOrdering.NullsLargest })
        foreach (var pageSize in new[] { 1, 5, 7 })
            data.Add(keyDescending, secondDescending, ordering, pageSize);
        return data;
    }

    [Theory, MemberData(nameof(Cases))]
    public void PagesEveryRowExactlyOnce_ForwardAndBackward(
        bool keyDescending, bool secondDescending, SeekNullOrdering ordering, int pageSize)
    {
        ISortColumn<Row>[] columns =
        [
            new SortColumn<Row, int?>("Key", r => r.Key, keyDescending),
            new SortColumn<Row, int?>("Second", r => r.Second, secondDescending),
            new SortColumn<Row, int>("Id", r => r.Id, isDescending: false),
        ];

        // The order the database itself would return.
        var expected = Sort(Rows, columns, ordering, reverse: false);

        // Forward.
        var forward = new List<Row>();
        Row? boundary = null;
        while (true)
        {
            var page = Fetch(expected, columns, ordering, boundary, SeekDirection.Next, pageSize);
            if (page.Count == 0) break;
            forward.AddRange(page);
            boundary = page[^1];
            Assert.True(forward.Count <= Rows.Length, "forward paging repeats rows");
        }

        Assert.Equal(expected.Select(r => r.Id), forward.Select(r => r.Id));

        // Backward, starting just before the very last row.
        var reversed = Sort(Rows, columns, ordering, reverse: true);
        var backward = new List<Row> { expected[^1] };
        boundary = expected[^1];
        while (true)
        {
            var page = Fetch(reversed, columns, ordering, boundary, SeekDirection.Previous, pageSize);
            if (page.Count == 0) break;
            backward.AddRange(page);
            boundary = page[^1];
            Assert.True(backward.Count <= Rows.Length, "backward paging repeats rows");
        }

        backward.Reverse();
        Assert.Equal(expected.Select(r => r.Id), backward.Select(r => r.Id));
    }

    [Fact]
    public void NonNullableColumns_KeepThePlainComparison()
    {
        // No IS NULL branch may sneak into a filter over columns that can't be null.
        ISortColumn<Row>[] columns = [new SortColumn<Row, int>("Id", r => r.Id, isDescending: false)];

        var predicate = KeysetPredicateBuilder.BuildOrPredicate(columns, Cursor(columns, Rows[4], SeekDirection.Next, SeekNullOrdering.NullsLargest), Converter)!;

        Assert.DoesNotContain("null", predicate.Body.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static List<Row> Fetch(
        List<Row> ordered, ISortColumn<Row>[] columns, SeekNullOrdering ordering,
        Row? boundary, SeekDirection direction, int pageSize)
    {
        if (boundary is null)
            return ordered.Take(pageSize).ToList();

        var predicate = KeysetPredicateBuilder
            .BuildOrPredicate(columns, Cursor(columns, boundary, direction, ordering), Converter)!
            .Compile();

        return ordered.Where(predicate).Take(pageSize).ToList();
    }

    private static SeekData Cursor(ISortColumn<Row>[] columns, Row boundary, SeekDirection direction, SeekNullOrdering ordering)
    {
        var values = new Dictionary<string, string>();
        foreach (var column in columns)
            values[column.PropertyPath] = Convert.ToString(column.GetValue(boundary), CultureInfo.InvariantCulture)!;

        // Convert.ToString(null) is "", which is how tokens issued before 2.4 spelled NULL;
        // the real cursor writer stores null. Exercise the current form.
        foreach (var column in columns)
            if (column.GetValue(boundary) is null)
                values[column.PropertyPath] = null!;

        return new SeekData { Direction = direction, Values = values, NullOrdering = ordering };
    }

    private static List<Row> Sort(Row[] rows, ISortColumn<Row>[] columns, SeekNullOrdering ordering, bool reverse)
    {
        int Compare(Row a, Row b)
        {
            foreach (var column in columns)
            {
                var x = (int?)column.GetValue(a);
                var y = (int?)column.GetValue(b);

                int result;
                if (x is null && y is null) result = 0;
                else if (x is null) result = ordering == SeekNullOrdering.NullsSmallest ? -1 : 1;
                else if (y is null) result = ordering == SeekNullOrdering.NullsSmallest ? 1 : -1;
                else result = x.Value.CompareTo(y.Value);

                if (column.IsDescending) result = -result;
                if (result != 0) return reverse ? -result : result;
            }
            return 0;
        }

        var list = rows.ToList();
        list.Sort(Compare);
        return list;
    }
}
