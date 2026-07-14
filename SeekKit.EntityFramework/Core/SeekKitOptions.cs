namespace SeekKit.EntityFramework.Core;

/// <summary>
/// Global configuration options for SeekKit cursor pagination with EF Core.
/// Register via <c>services.AddSeekKit(options => { ... })</c>.
/// Page-size options are inherited from <see cref="SeekOptionsBase"/>.
/// </summary>
public sealed class SeekKitOptions : SeekOptionsBase
{
    public SeekKitOptions() { }

    public SeekKitOptions(SeekKitOptions options) : base(options)
    {
        Strategy = options.Strategy;
    }

    /// <summary>
    /// Database-specific query strategy. Use one of the static factory methods on
    /// <see cref="DatabaseStrategy"/> to create a strategy for your database:
    /// <list type="bullet">
    ///   <item><see cref="DatabaseStrategy.ForSqlServer"/></item>
    ///   <item><see cref="DatabaseStrategy.ForPostgreSql"/></item>
    ///   <item><see cref="DatabaseStrategy.ForMySql"/></item>
    ///   <item><see cref="DatabaseStrategy.ForOracle"/></item>
    ///   <item><see cref="DatabaseStrategy.ForSqlite"/></item>
    /// </list>
    /// Default: <see cref="DatabaseStrategy.ForSqlServer"/> with <see cref="SqlServerStrategy.UnionAll"/>.
    /// </summary>
    public DatabaseStrategy Strategy { get; set; } = DatabaseStrategy.ForSqlServer();
}


/// <summary>
/// Query strategy options available for PostgreSQL.
/// </summary>
public enum PostgreSqlStrategy
{
    /// <summary>
    /// Automatically uses <see cref="Tuple"/> when all order columns share the same direction,
    /// otherwise falls back to <see cref="FallbackStrategy"/>. Recommended default (~0.6–0.9 ms).
    /// </summary>
    Auto,

    /// <summary>
    /// Row-value (tuple) comparison: <c>WHERE (a, b) &gt; (@a, @b)</c>.
    /// Fastest option (~0.6 ms) but requires all columns to sort in the same direction.
    /// Throws if mixed directions and <see cref="FallbackStrategy.None"/> is set.
    /// </summary>
    Tuple,

    /// <summary>
    /// UNION ALL with a <c>LIMIT</c> per branch (~0.9 ms).
    /// Handles mixed sort directions.
    /// </summary>
    UnionAll,

    /// <summary>
    /// OR-predicate expansion (~2 ms). Most compatible, no UNION overhead.
    /// </summary>
    OrLogic
}


/// <summary>
/// Query strategy options available for SQL Server.
/// </summary>
public enum SqlServerStrategy
{
    /// <summary>
    /// UNION ALL with <c>TOP N</c> per branch. Fastest option (~5-8 ms).
    /// </summary>
    UnionAll,

    /// <summary>
    /// OR-predicate expansion (~10-15 ms). More compatible but slower.
    /// </summary>
    OrLogic
}


/// <summary>
/// Query strategy options available for MySQL.
/// </summary>
public enum MySqlStrategy
{
    /// <summary>
    /// UNION ALL with <c>LIMIT</c> per branch. Recommended default.
    /// </summary>
    UnionAll,

    /// <summary>
    /// OR-predicate expansion. Most compatible.
    /// </summary>
    OrLogic
}


/// <summary>
/// Query strategy options available for Oracle.
/// </summary>
public enum OracleStrategy
{
    /// <summary>
    /// UNION ALL with <c>FETCH FIRST n ROWS ONLY</c> per branch. Recommended default.
    /// </summary>
    UnionAll,

    /// <summary>
    /// OR-predicate expansion. Most compatible.
    /// </summary>
    OrLogic
}


/// <summary>
/// Query strategy options available for SQLite.
/// </summary>
public enum SqliteStrategy
{
    /// <summary>
    /// UNION ALL with <c>LIMIT</c> per branch. Recommended default.
    /// </summary>
    UnionAll,

    /// <summary>
    /// OR-predicate expansion. Most compatible.
    /// </summary>
    OrLogic
}


/// <summary>
/// Strategy to apply when the primary strategy fails or cannot be used.
/// </summary>
public enum FallbackStrategy
{
    /// <summary>No fallback — throw an exception if the primary strategy fails.</summary>
    None,

    /// <summary>
    /// Fall back to UNION ALL. Works for all supported databases and handles
    /// mixed sort directions. Fast and recommended.
    /// </summary>
    UnionAll,

    /// <summary>
    /// Fall back to OR-predicate expansion. Universal compatibility, slower than UnionAll.
    /// </summary>
    OrLogic
}
