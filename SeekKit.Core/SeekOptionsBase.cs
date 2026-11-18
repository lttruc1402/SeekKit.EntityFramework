namespace SeekKit.Core;

/// <summary>
/// Provider-agnostic pagination options shared by all SeekKit provider packages
/// (SeekKit.EntityFramework, SeekKit.MongoDB, ...). Provider packages derive
/// their own options type from this class.
/// </summary>
public abstract class SeekOptionsBase
{
    protected SeekOptionsBase() { }

    protected SeekOptionsBase(SeekOptionsBase options)
    {
        DefaultPageSize      = options.DefaultPageSize;
        MaxPageSize          = options.MaxPageSize;
        MinPageSize          = options.MinPageSize;
        InvalidTokenBehavior = options.InvalidTokenBehavior;
        TokenLifetime        = options.TokenLifetime;
        NullOrdering         = options.NullOrdering;
        UtcNow               = options.UtcNow;
    }

    /// <summary>
    /// Number of items per page when the client does not specify <see cref="SeekRequest.PageSize"/>.
    /// Default: <c>10</c>.
    /// </summary>
    public int DefaultPageSize { get; set; } = 10;

    /// <summary>
    /// Upper bound for <see cref="SeekRequest.PageSize"/>. Requests above this value are clamped.
    /// Default: <c>1000</c>.
    /// </summary>
    public int MaxPageSize { get; set; } = 1000;

    /// <summary>
    /// Lower bound for <see cref="SeekRequest.PageSize"/>. Requests below this value are clamped.
    /// Default: <c>1</c>.
    /// </summary>
    public int MinPageSize { get; set; } = 1;

    /// <summary>
    /// What happens when a token can't be decoded (truncated, foreign, tampered) or has
    /// expired. Default: <see cref="SeekInvalidTokenBehavior.FirstPage"/> — the token is
    /// ignored. Set to <see cref="SeekInvalidTokenBehavior.Throw"/> to get a
    /// <see cref="SeekTokenException"/> instead and answer with <c>400 Bad Request</c>.
    /// </summary>
    /// <remarks>
    /// A token issued for a different sort than the current query always throws
    /// <see cref="SeekTokenException"/>, whatever this is set to — silently paging from the
    /// wrong position is never acceptable.
    /// </remarks>
    public SeekInvalidTokenBehavior InvalidTokenBehavior { get; set; } = SeekInvalidTokenBehavior.FirstPage;

    /// <summary>
    /// How long an issued token stays valid. Default: <see langword="null"/> — tokens never
    /// expire. When set, every new token carries its expiry, and a token that is past it —
    /// or that carries no expiry at all — is treated per <see cref="InvalidTokenBehavior"/>.
    /// </summary>
    /// <remarks>
    /// The expiry lives inside the token, so it is only tamper-proof together with
    /// <c>UseHmacSigning</c>; without signing a client can edit it. A custom
    /// <see cref="ISeekSerializer"/> must round-trip <see cref="SeekData.ExpiresAt"/>.
    /// </remarks>
    public TimeSpan? TokenLifetime
    {
        get => _tokenLifetime;
        set
        {
            if (value is { } lifetime && lifetime <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), "TokenLifetime must be positive, or null for tokens that never expire.");

            _tokenLifetime = value;
        }
    }

    private TimeSpan? _tokenLifetime;

    /// <summary>
    /// Where the database places <c>NULL</c> when sorting. Default: <see langword="null"/> —
    /// use the provider's native behaviour (see <see cref="DefaultNullOrdering"/>). Override
    /// only if your database or collation sorts <c>NULL</c> the other way round.
    /// </summary>
    public SeekNullOrdering? NullOrdering { get; set; }

    /// <summary>
    /// The null ordering used when <see cref="NullOrdering"/> is not set. Provider option
    /// types override this to match their database.
    /// </summary>
    protected virtual SeekNullOrdering DefaultNullOrdering => SeekNullOrdering.NullsSmallest;

    /// <summary>The clock used for token expiry. Replaceable in tests.</summary>
    internal Func<DateTimeOffset> UtcNow { get; set; } = static () => DateTimeOffset.UtcNow;

    internal SeekNullOrdering ResolveNullOrdering() => NullOrdering ?? DefaultNullOrdering;
}
