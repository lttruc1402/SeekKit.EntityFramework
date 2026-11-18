namespace SeekKit.EntityFramework.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SeekSortMap{T}"/> — parsing and validating a client's sort
/// string against a whitelist. The one accepted spelling is <c>field:asc</c> /
/// <c>field:desc</c>, comma-separated.
/// </summary>
public sealed class SeekSortMapTests
{
    private static SeekSortMap<Product> Map() => new SeekSortMap<Product>()
        .Add("createdAt", p => p.CreatedAt)
        .Add("price", p => p.Price)
        .Add("name", p => p.Name)
        .Add("id", p => p.Id)
        .Default("createdAt:desc")
        .TieBreaker("id");

    private static string Resolve(SeekSortMap<Product> map, string? sort)
        => string.Join(",", map.Resolve(sort));

    [Theory]
    [InlineData("price:asc", "price:asc,id:asc")]
    [InlineData("price:desc", "price:desc,id:desc")]
    [InlineData("price", "price:asc,id:asc")]                       // direction left out
    [InlineData("price:DESC", "price:desc,id:desc")]
    [InlineData("PRICE:Asc,Name:desc", "price:asc,name:desc,id:asc")]
    [InlineData("  price : desc ,  name:asc  ", "price:desc,name:asc,id:desc")]
    [InlineData("price:desc,,name:asc,", "price:desc,name:asc,id:desc")]
    public void ParsesTheFieldDirectionSyntax(string sort, string expected)
        => Assert.Equal(expected, Resolve(Map(), sort));

    [Theory]
    [InlineData("-price")]
    [InlineData("+price")]
    [InlineData("price desc")]
    [InlineData("price DESC")]
    [InlineData("-price:desc")]
    public void OtherSpellings_AreRejected_AndSayWhatToWrite(string sort)
    {
        var ex = Assert.Throws<SeekSortException>(() => Map().Resolve(sort));

        Assert.Contains("'field:asc' or 'field:desc'", ex.Message);
    }

    [Theory]
    [InlineData("price:sideways")]
    [InlineData("price:descending")]
    [InlineData("price:")]
    [InlineData(":desc")]
    [InlineData("price:desc:asc")]
    [InlineData("price;drop table products")]
    public void MalformedEntry_IsRejected(string sort)
        => Assert.Throws<SeekSortException>(() => Map().Resolve(sort));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoSort_UsesTheDefault(string? sort)
        => Assert.Equal("createdAt:desc,id:desc", Resolve(Map(), sort));

    [Fact]
    public void TieBreaker_IsNotRepeated_WhenTheClientSortsByIt()
        => Assert.Equal("name:asc,id:desc", Resolve(Map(), "name:asc,id:desc"));

    [Fact]
    public void TieBreaker_FollowsTheFirstFieldsDirection_UnlessPinned()
    {
        Assert.Equal("name:desc,price:asc,id:desc", Resolve(Map(), "name:desc,price:asc"));

        var pinned = new SeekSortMap<Product>()
            .Add("name", p => p.Name)
            .Add("id", p => p.Id)
            .TieBreaker("id", descending: false);

        Assert.Equal("name:desc,id:asc", Resolve(pinned, "name:desc"));
    }

    [Fact]
    public void NoSortAndNoDefault_FallsBackToTheTieBreakerAlone()
    {
        var map = new SeekSortMap<Product>().Add("id", p => p.Id).TieBreaker("id");

        Assert.Equal("id:asc", Resolve(map, null));
    }

    [Fact]
    public void UnknownField_IsRejected_AndListsWhatIsSortable()
    {
        var ex = Assert.Throws<SeekSortException>(() => Map().Resolve("passwordHash:desc"));

        Assert.Equal("passwordHash", ex.Field);
        Assert.Contains("createdAt, price, name, id", ex.Message);
        Assert.IsAssignableFrom<ArgumentException>(ex);
    }

    [Theory]
    [InlineData("price:asc,price:asc")]
    [InlineData("price:asc,PRICE:desc")]
    public void DuplicateField_IsRejected(string sort)
        => Assert.Throws<SeekSortException>(() => Map().Resolve(sort));

    [Fact]
    public void TooManyFields_IsRejected()
    {
        var map = Map().LimitTo(2);

        Assert.Equal("price:asc,name:asc,id:asc", Resolve(map, "price:asc,name:asc"));   // tie-breaker doesn't count
        Assert.Throws<SeekSortException>(() => map.Resolve("price:asc,name:asc,createdAt:asc"));
    }

    [Fact]
    public void Misconfiguration_IsNotBlamedOnTheClient()
    {
        // Tie-breaker that was never registered.
        var badTieBreaker = new SeekSortMap<Product>().Add("name", p => p.Name).TieBreaker("id");
        Assert.Throws<InvalidOperationException>(() => badTieBreaker.Resolve("name:asc"));

        // Nothing to sort by at all.
        var empty = new SeekSortMap<Product>().Add("name", p => p.Name);
        Assert.Throws<InvalidOperationException>(() => empty.Resolve(null));

        Assert.Throws<ArgumentException>(() => new SeekSortMap<Product>().Add("name", p => p.Name).Add("NAME", p => p.Name));
        Assert.Throws<ArgumentException>(() => new SeekSortMap<Product>().Add("a,b", p => p.Name));
        Assert.Throws<ArgumentException>(() => new SeekSortMap<Product>().Add("a:b", p => p.Name));
        Assert.Throws<ArgumentException>(() => new SeekSortMap<Product>().Add("-a", p => p.Name));
    }

    [Fact]
    public void Fields_ListsRegisteredNamesInOrder()
        => Assert.Equal(["createdAt", "price", "name", "id"], Map().Fields);
}
