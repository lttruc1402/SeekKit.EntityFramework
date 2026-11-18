namespace SeekKit.EntityFramework.Tests.Integration;

/// <summary>
/// What happens to a token that shouldn't be used: issued for another sort, malformed,
/// expired, or holding a value of the wrong type.
/// </summary>
public sealed class TokenValidationTests : IClassFixture<SeekFixture>, IDisposable
{
    private readonly ISeekFactory    _factory;
    private readonly ISeekSerializer _serializer;
    private readonly TestDbContext   _db;

    public TokenValidationTests(SeekFixture fixture)
    {
        _factory    = fixture.Factory;
        _serializer = fixture.ServiceProvider.GetRequiredService<ISeekSerializer>();
        _db         = new TestDbContext();
        _db.Database.EnsureCreated();
        _db.Seed(count: 30);
    }

    // ── Sort fingerprint ──────────────────────────────────────────────────────

    [Fact]
    public async Task TokenFromAnotherDirection_IsRejected()
    {
        var first = await _factory.CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        // Same column, opposite direction: the keys in the token still match by name, so
        // before the fingerprint this silently returned the wrong rows.
        var ex = await Assert.ThrowsAsync<SeekTokenException>(async () => await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = first.NextToken, PageSize = 5 })
            .OrderByDescending(p => p.Id)
            .ToSeekResultAsync());

        Assert.Equal(SeekTokenError.SortMismatch, ex.Error);
    }

    [Fact]
    public async Task TokenFromOtherColumns_IsRejected()
    {
        var first = await _factory.CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        var ex = await Assert.ThrowsAsync<SeekTokenException>(async () => await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = first.NextToken, PageSize = 5 })
            .OrderBy(p => p.CreatedAt)
            .OrderBy(p => p.Id)
            .ToSeekResultAsync());

        Assert.Equal(SeekTokenError.SortMismatch, ex.Error);
    }

    [Fact]
    public async Task TokenFromSameSort_DifferentPageSize_IsAccepted()
    {
        var first = await _factory.CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        var second = await _factory.CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = first.NextToken, PageSize = 3 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal([6, 7, 8], second.Items.Select(p => p.Id));
    }

    [Fact]
    public async Task TokenWithoutFingerprint_FromBefore24_StillWorks()
    {
        var legacy = _serializer.Serialize(new SeekData
        {
            Direction = SeekDirection.Next,
            Values = new Dictionary<string, string> { ["Id"] = "10" },
        });

        var page = await _factory.CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = legacy, PageSize = 3 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal([11, 12, 13], page.Items.Select(p => p.Id));
    }

    [Fact]
    public async Task LegacyTokenMissingAColumn_IsRejectedAsSortMismatch()
    {
        var legacy = _serializer.Serialize(new SeekData
        {
            Direction = SeekDirection.Next,
            Values = new Dictionary<string, string> { ["Id"] = "10" },
        });

        var ex = await Assert.ThrowsAsync<SeekTokenException>(async () => await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = legacy, PageSize = 3 })
            .OrderBy(p => p.CreatedAt)
            .OrderBy(p => p.Id)
            .ToSeekResultAsync());

        Assert.Equal(SeekTokenError.SortMismatch, ex.Error);
    }

    [Fact]
    public async Task TokenWithValueOfWrongType_IsRejectedAsInvalidValue()
    {
        var forged = _serializer.Serialize(new SeekData
        {
            Direction = SeekDirection.Next,
            Values = new Dictionary<string, string> { ["Id"] = "not-a-number" },
        });

        var ex = await Assert.ThrowsAsync<SeekTokenException>(async () => await _factory
            .CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = forged, PageSize = 3 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync());

        Assert.Equal(SeekTokenError.InvalidValue, ex.Error);
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
    }

    // ── Malformed tokens ──────────────────────────────────────────────────────

    [Fact]
    public async Task MalformedToken_ByDefault_FallsBackToARealFirstPage()
    {
        var page = await _factory.CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { Token = "this-is-not-a-token", PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal([1, 2, 3, 4, 5], page.Items.Select(p => p.Id));
        // It IS the first page, so it must not advertise a page before it.
        Assert.False(page.HasPrevious);
        Assert.Null(page.PreviousToken);
    }

    [Fact]
    public async Task MalformedToken_InStrictMode_Throws()
    {
        var ex = await Assert.ThrowsAsync<SeekTokenException>(async () => await _factory
            .CreateBuilder(_db.Products, o => o.InvalidTokenBehavior = SeekInvalidTokenBehavior.Throw)
            .WithRequest(new SeekRequest { Token = "this-is-not-a-token", PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync());

        Assert.Equal(SeekTokenError.Malformed, ex.Error);
    }

    // ── Expiry ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Token_WithinLifetime_IsAccepted_ThenExpires()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        void Configure(SeekKitOptions o)
        {
            o.TokenLifetime        = TimeSpan.FromMinutes(5);
            o.InvalidTokenBehavior = SeekInvalidTokenBehavior.Throw;
            o.UtcNow               = () => now;
        }

        var first = await _factory.CreateBuilder(_db.Products, Configure)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        now = now.AddMinutes(4);
        var second = await _factory.CreateBuilder(_db.Products, Configure)
            .WithRequest(new SeekRequest { Token = first.NextToken, PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();
        Assert.Equal(6, second.Items[0].Id);

        now = now.AddMinutes(2);   // 6 minutes after issue
        var ex = await Assert.ThrowsAsync<SeekTokenException>(async () => await _factory
            .CreateBuilder(_db.Products, Configure)
            .WithRequest(new SeekRequest { Token = first.NextToken, PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync());

        Assert.Equal(SeekTokenError.Expired, ex.Error);
    }

    [Fact]
    public async Task ExpiredToken_ByDefault_FallsBackToFirstPage()
    {
        var now = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

        void Configure(SeekKitOptions o)
        {
            o.TokenLifetime = TimeSpan.FromMinutes(5);
            o.UtcNow        = () => now;
        }

        var first = await _factory.CreateBuilder(_db.Products, Configure)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        now = now.AddHours(1);
        var again = await _factory.CreateBuilder(_db.Products, Configure)
            .WithRequest(new SeekRequest { Token = first.NextToken, PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        Assert.Equal(1, again.Items[0].Id);
        Assert.False(again.HasPrevious);
    }

    [Fact]
    public async Task TokenWithoutExpiry_IsRejectedOnceALifetimeIsConfigured()
    {
        // Issued while tokens didn't expire.
        var first = await _factory.CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        var ex = await Assert.ThrowsAsync<SeekTokenException>(async () => await _factory
            .CreateBuilder(_db.Products, o =>
            {
                o.TokenLifetime        = TimeSpan.FromMinutes(5);
                o.InvalidTokenBehavior = SeekInvalidTokenBehavior.Throw;
            })
            .WithRequest(new SeekRequest { Token = first.NextToken, PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync());

        Assert.Equal(SeekTokenError.Expired, ex.Error);
    }

    [Fact]
    public async Task WithoutALifetime_TokensCarryNoExpiry()
    {
        var first = await _factory.CreateBuilder(_db.Products)
            .WithRequest(new SeekRequest { PageSize = 5 })
            .OrderBy(p => p.Id)
            .ToSeekResultAsync();

        var data = _serializer.Deserialize(first.NextToken!);

        Assert.Null(data.ExpiresAt);
        Assert.NotNull(data.Sort);
    }

    [Fact]
    public void NonPositiveLifetime_IsRejectedAtConfiguration()
    {
        var options = new SeekKitOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options.TokenLifetime = TimeSpan.Zero);
    }

    public void Dispose() => _db.Dispose();
}
