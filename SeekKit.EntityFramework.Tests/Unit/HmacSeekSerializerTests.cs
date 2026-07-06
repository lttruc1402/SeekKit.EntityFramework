namespace SeekKit.EntityFramework.Tests.Unit;

/// <summary>
/// Unit tests for <see cref="HmacSeekSerializer"/> — HMAC-SHA256-signed tokens.
/// </summary>
public sealed class HmacSeekSerializerTests
{
    private static readonly byte[] Key = "test-key-0123456789-abcdefghijkl"u8.ToArray();

    private readonly ISeekSerializer _sut = new HmacSeekSerializer(Key);

    private static SeekData SampleData() => new()
    {
        Direction = SeekDirection.Next,
        Values    = new Dictionary<string, string>
        {
            ["Id"]   = "42",
            ["Name"] = "Hello World"
        }
    };

    // ── Round-trip ────────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_ThenDeserialize_ReturnsOriginalData()
    {
        var token  = _sut.Serialize(SampleData());
        var result = _sut.Deserialize(token);

        Assert.Equal(SeekDirection.Next, result.Direction);
        Assert.Equal("42",          result.Values["Id"]);
        Assert.Equal("Hello World", result.Values["Name"]);
    }

    [Fact]
    public void Serialize_Token_HasPayloadAndSignatureParts()
    {
        var token = _sut.Serialize(SampleData());

        var parts = token.Split('.');
        Assert.Equal(2, parts.Length);
        Assert.NotEmpty(parts[0]);
        Assert.NotEmpty(parts[1]);
    }

    [Fact]
    public void Serialize_Token_ContainsOnlyUrlSafeChars()
    {
        var token = _sut.Serialize(SampleData());

        Assert.All(token, c => Assert.True(
            char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.',
            $"Unexpected character '{c}' in token"));
    }

    // ── Tamper resistance ─────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_TamperedPayload_ReturnsEmpty()
    {
        var token = _sut.Serialize(SampleData());
        var parts = token.Split('.');

        // Forge the payload: client changes the keyset values but keeps the signature
        var forgedPayload = new SeekSerializer().Serialize(new SeekData
        {
            Direction = SeekDirection.Next,
            Values    = new Dictionary<string, string> { ["Id"] = "999999" }
        });
        var tampered = forgedPayload + "." + parts[1];

        var result = _sut.Deserialize(tampered);

        Assert.Empty(result.Values);   // rejected → falls back to first page
    }

    [Fact]
    public void Deserialize_TamperedSignature_ReturnsEmpty()
    {
        var token = _sut.Serialize(SampleData());
        var parts = token.Split('.');

        // Flip a character in the signature
        var sigChars = parts[1].ToCharArray();
        sigChars[0] = sigChars[0] == 'A' ? 'B' : 'A';
        var tampered = parts[0] + "." + new string(sigChars);

        var result = _sut.Deserialize(tampered);

        Assert.Empty(result.Values);
    }

    [Fact]
    public void Deserialize_TokenSignedWithDifferentKey_ReturnsEmpty()
    {
        var otherKey = "another-secret-key-9876543210-xx"u8.ToArray();
        var foreign  = new HmacSeekSerializer(otherKey).Serialize(SampleData());

        var result = _sut.Deserialize(foreign);

        Assert.Empty(result.Values);
    }

    [Fact]
    public void Deserialize_UnsignedTokenFromDefaultSerializer_ReturnsEmpty()
    {
        // A plain (unsigned) token must not be accepted by the HMAC serializer
        var unsigned = new SeekSerializer().Serialize(SampleData());

        var result = _sut.Deserialize(unsigned);

        Assert.Empty(result.Values);
    }

    // ── Defensive cases ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-separator")]
    [InlineData(".only-signature")]
    [InlineData("only-payload.")]
    [InlineData("not!valid.base64!!")]
    public void Deserialize_MalformedToken_DoesNotThrow_ReturnsEmpty(string? cursor)
    {
        var result = _sut.Deserialize(cursor!);

        Assert.NotNull(result);
        Assert.Empty(result.Values);
    }

    [Fact]
    public void Serialize_NullSeekData_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, _sut.Serialize(null!));
    }

    // ── Key validation ────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new HmacSeekSerializer(null!));
    }

    [Fact]
    public void Constructor_ShortKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => new HmacSeekSerializer("short"u8.ToArray()));
    }
}
