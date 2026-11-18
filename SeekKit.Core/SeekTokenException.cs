namespace SeekKit.Core;

/// <summary>Why a pagination token was rejected.</summary>
public enum SeekTokenError
{
    /// <summary>
    /// The token could not be decoded: it is truncated, not a SeekKit token, or (with HMAC
    /// signing enabled) its signature does not match.
    /// </summary>
    Malformed,

    /// <summary>
    /// The token is older than <see cref="SeekOptionsBase.TokenLifetime"/>, or carries no
    /// expiry at all while a lifetime is configured.
    /// </summary>
    Expired,

    /// <summary>
    /// The token was issued for a different sort (other columns, another order, or other
    /// directions) than the one the current query uses.
    /// </summary>
    SortMismatch,

    /// <summary>
    /// A keyset value inside the token cannot be converted to its sort column's type.
    /// </summary>
    InvalidValue,
}

/// <summary>
/// Thrown when a client-supplied pagination token cannot be used. The problem is always
/// with the request, never with the server, so an API should answer it with
/// <c>400 Bad Request</c>.
/// </summary>
/// <remarks>
/// <see cref="SeekTokenError.SortMismatch"/> and <see cref="SeekTokenError.InvalidValue"/>
/// are always thrown. <see cref="SeekTokenError.Malformed"/> and
/// <see cref="SeekTokenError.Expired"/> are thrown only when
/// <see cref="SeekOptionsBase.InvalidTokenBehavior"/> is
/// <see cref="SeekInvalidTokenBehavior.Throw"/>; by default such tokens silently fall back
/// to the first page.
/// </remarks>
public sealed class SeekTokenException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    public SeekTokenException(SeekTokenError error, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Error = error;
    }

    /// <summary>Why the token was rejected.</summary>
    public SeekTokenError Error { get; }
}

/// <summary>
/// What to do with a token that can't be decoded or has expired.
/// </summary>
public enum SeekInvalidTokenBehavior
{
    /// <summary>Ignore the token and return the first page (the default).</summary>
    FirstPage,

    /// <summary>Throw <see cref="SeekTokenException"/> so the API can reject the request.</summary>
    Throw,
}

/// <summary>
/// Where the database places <c>NULL</c> relative to every other value when sorting.
/// SeekKit never changes the <c>ORDER BY</c> itself — it needs to know this only to build a
/// keyset filter that agrees with it for nullable sort columns.
/// </summary>
public enum SeekNullOrdering
{
    /// <summary>
    /// <c>NULL</c> sorts before everything else in ascending order — SQL Server, MySQL,
    /// SQLite, MongoDB, and LINQ to Objects.
    /// </summary>
    NullsSmallest,

    /// <summary>
    /// <c>NULL</c> sorts after everything else in ascending order — PostgreSQL and Oracle.
    /// </summary>
    NullsLargest,
}
