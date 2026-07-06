namespace SeekKit.EntityFramework.Core;

/// <summary>
/// Base class for database-specific query strategy configuration.
/// Use the static factory methods to create a strategy for your database:
/// <see cref="ForPostgreSql"/>, <see cref="ForSqlServer"/>,
/// <see cref="ForMySql"/>, <see cref="ForOracle"/>, <see cref="ForSqlite"/>.
/// </summary>
/// <example>
/// <code>
/// services.AddSeekKit(options =>
/// {
///     options.Strategy = DatabaseStrategy.ForPostgreSql(
///         strategy: PostgreSqlStrategy.Tuple,
///         fallback:  FallbackStrategy.OrLogic);
/// });
/// </code>
/// </example>
public abstract class DatabaseStrategy
{
    // Prevent external subclassing — only internal concrete types are valid.
    private protected DatabaseStrategy(FallbackStrategy fallback = FallbackStrategy.OrLogic)
    {
        Fallback = fallback;
    }

    /// <summary>
    /// Strategy to use when the primary strategy cannot be applied
    /// (e.g. tuple comparison with mixed sort directions).
    /// Default: <see cref="FallbackStrategy.UnionAll"/>.
    /// </summary>
    public FallbackStrategy Fallback { get;  }

    /// <summary>The target database engine for this strategy.</summary>
    public abstract DatabaseType DatabaseType { get; }

    /// <summary>
    /// Builds the <see cref="ISeekFilterStrategy"/> for this configuration.
    /// </summary>
    internal ISeekFilterStrategy GetFilterStrategy(ISeekValueConverter converter)
    {
        return GetFilterStrategyCore(converter);
    }


    protected abstract ISeekFilterStrategy GetFilterStrategyCore(ISeekValueConverter converter);

    // ── Static factory methods ────────────────────────────────────────────────

    /// <summary>Creates a PostgreSQL strategy configuration.</summary>
    /// <param name="strategy">Query strategy. Default: <see cref="PostgreSqlStrategy.Auto"/>.</param>
    /// <param name="fallback">Fallback when primary cannot be applied. Default: <see cref="FallbackStrategy.OrLogic"/>.</param>
    public static DatabaseStrategy ForPostgreSql(
        PostgreSqlStrategy strategy = PostgreSqlStrategy.Auto,
        FallbackStrategy   fallback = FallbackStrategy.OrLogic)
        => new SeekPostgreSqlStrategy(strategy, fallback);

    /// <summary>Creates a SQL Server strategy configuration.</summary>
    /// <param name="strategy">Query strategy. Default: <see cref="SqlServerStrategy.UnionAll"/> (~5-8 ms).</param>
    /// <param name="fallback">Fallback when primary cannot be applied. Default: <see cref="FallbackStrategy.OrLogic"/>.</param>
    public static DatabaseStrategy ForSqlServer(
        SqlServerStrategy strategy = SqlServerStrategy.UnionAll,
        FallbackStrategy  fallback = FallbackStrategy.OrLogic)
        => new SeekSqlServerStrategy(strategy, fallback);

    /// <summary>Creates a MySQL strategy configuration.</summary>
    /// <param name="strategy">Query strategy. Default: <see cref="MySqlStrategy.UnionAll"/>.</param>
    /// <param name="fallback">Fallback when primary cannot be applied. Default: <see cref="FallbackStrategy.OrLogic"/>.</param>
    public static DatabaseStrategy ForMySql(
        MySqlStrategy    strategy = MySqlStrategy.UnionAll,
        FallbackStrategy fallback = FallbackStrategy.OrLogic)
        => new SeekMySqlStrategy(strategy, fallback);

    /// <summary>Creates an Oracle strategy configuration.</summary>
    /// <param name="strategy">Query strategy. Default: <see cref="OracleStrategy.UnionAll"/>.</param>
    /// <param name="fallback">Fallback when primary cannot be applied. Default: <see cref="FallbackStrategy.OrLogic"/>.</param>
    public static DatabaseStrategy ForOracle(
        OracleStrategy   strategy = OracleStrategy.UnionAll,
        FallbackStrategy fallback = FallbackStrategy.OrLogic)
        => new SeekOracleStrategy(strategy, fallback);

    /// <summary>Creates a SQLite strategy configuration.</summary>
    /// <param name="strategy">Query strategy. Default: <see cref="SqliteStrategy.UnionAll"/>.</param>
    /// <param name="fallback">Fallback when primary cannot be applied. Default: <see cref="FallbackStrategy.OrLogic"/>.</param>
    public static DatabaseStrategy ForSqlite(
        SqliteStrategy strategy = SqliteStrategy.UnionAll,
        FallbackStrategy fallback = FallbackStrategy.OrLogic)
        => new SeekSqliteStrategy(strategy, fallback);
}

// ── Internal concrete implementations ────────────────────────────────────────

internal sealed class SeekPostgreSqlStrategy : DatabaseStrategy
{
    internal SeekPostgreSqlStrategy(PostgreSqlStrategy strategy, FallbackStrategy fallback)
        : base(fallback)
    {
        Strategy = strategy;
    }
    public PostgreSqlStrategy Strategy { get;  } 
    public override DatabaseType DatabaseType => DatabaseType.PostgreSql;

    protected override ISeekFilterStrategy GetFilterStrategyCore(
        ISeekValueConverter converter)
    {
        return Strategy switch
        {
            PostgreSqlStrategy.Auto     => new PostgreSqlAutoStrategy(converter, Fallback),
            PostgreSqlStrategy.Tuple    => new PostgreSqlTupleSeekStrategy(converter, Fallback),
            PostgreSqlStrategy.UnionAll => new UnionAllSeekStrategy(converter, Fallback),
            PostgreSqlStrategy.OrLogic  => new OrLogicSeekStrategy(converter),
            _                           => new PostgreSqlAutoStrategy(converter, Fallback)
        };
    }
}

internal sealed class SeekSqlServerStrategy : DatabaseStrategy
{
    internal SeekSqlServerStrategy(SqlServerStrategy strategy, FallbackStrategy fallback)
        : base(fallback)
    {
        Strategy = strategy;
    }
    public SqlServerStrategy Strategy { get; }
    public override DatabaseType DatabaseType => DatabaseType.SqlServer;

    protected override ISeekFilterStrategy GetFilterStrategyCore(
        ISeekValueConverter converter)
    {
        return Strategy switch
        {
            SqlServerStrategy.UnionAll => new UnionAllSeekStrategy(converter, Fallback),
            SqlServerStrategy.OrLogic  => new OrLogicSeekStrategy(converter),
            _                          => new UnionAllSeekStrategy(converter, Fallback)
        };
    }
}

internal sealed class SeekMySqlStrategy : DatabaseStrategy
{
    internal SeekMySqlStrategy(MySqlStrategy strategy, FallbackStrategy fallback)
        : base(fallback)
    {
        Strategy = strategy;
    }
    public MySqlStrategy Strategy { get; } 
    public override DatabaseType DatabaseType => DatabaseType.MySql;

    protected override ISeekFilterStrategy GetFilterStrategyCore(
        ISeekValueConverter converter)
    {
        return Strategy switch
        {
            MySqlStrategy.UnionAll => new UnionAllSeekStrategy(converter, Fallback),
            MySqlStrategy.OrLogic  => new OrLogicSeekStrategy(converter),
            _                      => new UnionAllSeekStrategy(converter, Fallback)
        };
    }
}

internal sealed class SeekOracleStrategy : DatabaseStrategy
{
    public SeekOracleStrategy(OracleStrategy strategy, FallbackStrategy fallback)
        : base(fallback)
    {
        Strategy = strategy;
    }
    public OracleStrategy Strategy { get; }

    public override DatabaseType DatabaseType => DatabaseType.Oracle;

    protected override ISeekFilterStrategy GetFilterStrategyCore(
        ISeekValueConverter converter)
    {
        return Strategy switch
        {
            OracleStrategy.UnionAll => new UnionAllSeekStrategy(converter, Fallback),
            OracleStrategy.OrLogic  => new OrLogicSeekStrategy(converter),
            _                       => new UnionAllSeekStrategy(converter, Fallback)
        };
    }
}

internal sealed class SeekSqliteStrategy : DatabaseStrategy
{
   
    public SeekSqliteStrategy(SqliteStrategy strategy, FallbackStrategy fallback): base(fallback) 
    {
        Strategy = strategy;
    }
    public SqliteStrategy Strategy { get; }
    public override DatabaseType DatabaseType => DatabaseType.Sqlite;

    protected override ISeekFilterStrategy GetFilterStrategyCore(
        ISeekValueConverter converter)
    {
        return Strategy switch
        {
            SqliteStrategy.UnionAll => new UnionAllSeekStrategy(converter, Fallback),
            SqliteStrategy.OrLogic  => new OrLogicSeekStrategy(converter),
            _                       => new UnionAllSeekStrategy(converter, Fallback)
        };
    }
}
