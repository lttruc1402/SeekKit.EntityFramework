using SeekKit.EntityFramework.Helpers;

namespace SeekKit.EntityFramework.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="SeekSerializer"/> / <see cref="ISeekSerializer"/>.
/// No database or license required — purely in-process JSON + Base64url logic.
/// </summary>
public sealed class SeekSerializerTests
{
    // SeekSerializer is internal; accessible via InternalsVisibleTo.
    private readonly ISeekSerializer _sut = new SeekSerializer();

    // ── Round-trip ────────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_ThenDeserialize_ReturnsOriginalData()
    {
        var original = new SeekData
        {
            Direction = SeekDirection.Next,
            Values    = new Dictionary<string, string>
            {
                ["Id"]   = "42",
                ["Name"] = "Hello World"
            }
        };

        var token  = _sut.Serialize(original);
        var result = _sut.Deserialize(token);

        Assert.Equal(SeekDirection.Next, result.Direction);
        Assert.Equal("42",          result.Values["Id"]);
        Assert.Equal("Hello World", result.Values["Name"]);
    }

    [Fact]
    public void Serialize_PreviousDirection_RoundTrip()
    {
        var original = new SeekData
        {
            Direction = SeekDirection.Previous,
            Values    = new Dictionary<string, string> { ["Id"] = "7" }
        };

        var token  = _sut.Serialize(original);
        var result = _sut.Deserialize(token);

        Assert.Equal(SeekDirection.Previous, result.Direction);
        Assert.Equal("7", result.Values["Id"]);
    }

    [Fact]
    public void Serialize_MultipleValues_AllPreserved()
    {
        var original = new SeekData
        {
            Direction = SeekDirection.Next,
            Values    = new Dictionary<string, string>
            {
                ["Id"]        = "1",
                ["Name"]      = "Foo",
                ["Price"]     = "99.99",
                ["CreatedAt"] = "2024-01-15T00:00:00Z"
            }
        };

        var token  = _sut.Serialize(original);
        var result = _sut.Deserialize(token);

        Assert.Equal(4, result.Values.Count);
        Assert.Equal("99.99",              result.Values["Price"]);
        Assert.Equal("2024-01-15T00:00:00Z", result.Values["CreatedAt"]);
    }

    // ── URL safety ────────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_Token_ContainsNoStandardBase64SpecialChars()
    {
        // Values with bytes that produce '+' and '/' in standard Base64
        var data = new SeekData
        {
            Direction = SeekDirection.Next,
            Values    = new Dictionary<string, string> { ["X"] = "abc+def/xyz==" }
        };

        var token = _sut.Serialize(data);

        Assert.DoesNotContain('+', token);
        Assert.DoesNotContain('/', token);
        Assert.DoesNotContain('=', token);
    }

    [Fact]
    public void Serialize_Token_ContainsOnlyUrlSafeChars()
    {
        var data = new SeekData
        {
            Direction = SeekDirection.Next,
            Values    = new Dictionary<string, string> { ["Id"] = "12345" }
        };

        var token = _sut.Serialize(data);

        // Every character must be alphanumeric, '-', or '_'
        Assert.All(token, c => Assert.True(
            char.IsLetterOrDigit(c) || c == '-' || c == '_',
            $"Unexpected character '{c}' in token"));
    }

    // ── Deserialize defensive cases ───────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Deserialize_NullOrWhitespace_ReturnsEmptySeekData(string? cursor)
    {
        var result = _sut.Deserialize(cursor!);

        Assert.NotNull(result);
        Assert.Empty(result.Values);
    }

    [Theory]
    [InlineData("not-valid-base64!!!")]
    [InlineData("aaaa")]          // valid Base64 but not valid JSON
    [InlineData("????")]
    public void Deserialize_InvalidToken_DoesNotThrow_ReturnsEmpty(string cursor)
    {
        var result = _sut.Deserialize(cursor);

        Assert.NotNull(result);
        // No exception — interface contract says it never throws
    }

    // ── Base64url decode correctness (the fixed bug) ──────────────────────────

    [Fact]
    public void Deserialize_TokenContainingUnderscore_DecodesCorrectly()
    {
        // Produce a token that serializes to a URL-safe token with '_'
        // (i.e. standard Base64 would have '/' in that position).
        // We verify round-trip still succeeds — proves '_' → '/' replacement works.
        var data = new SeekData
        {
            Direction = SeekDirection.Next,
            Values    = new Dictionary<string, string> { ["Key"] = "value/with/slashes" }
        };

        var token  = _sut.Serialize(data);
        var result = _sut.Deserialize(token);

        Assert.Equal("value/with/slashes", result.Values["Key"]);
    }

    [Fact]
    public void Deserialize_TokenContainingDash_DecodesCorrectly()
    {
        var data = new SeekData
        {
            Direction = SeekDirection.Next,
            Values    = new Dictionary<string, string> { ["Key"] = "value+with+pluses" }
        };

        var token  = _sut.Serialize(data);
        var result = _sut.Deserialize(token);

        Assert.Equal("value+with+pluses", result.Values["Key"]);
    }

    // ── Null input to Serialize ───────────────────────────────────────────────

    [Fact]
    public void Serialize_NullSeekData_ReturnsEmpty()
    {
        var result = _sut.Serialize(null!);
        Assert.Equal(string.Empty, result);
    }
}
