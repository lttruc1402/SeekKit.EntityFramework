namespace SeekKit.EntityFramework.Core;

/// <summary>
/// Identifies the database engine. SeekKit uses this to select the optimal
/// pagination strategy and to translate LINQ expressions correctly.
/// </summary>
public enum DatabaseType : byte
{
    /// <summary>Microsoft SQL Server (via <c>Microsoft.EntityFrameworkCore.SqlServer</c>).</summary>
    SqlServer,

    /// <summary>PostgreSQL (via <c>Npgsql.EntityFrameworkCore.PostgreSQL</c>).</summary>
    PostgreSql,

    /// <summary>MySQL / MariaDB (via <c>Pomelo.EntityFrameworkCore.MySql</c>).</summary>
    MySql,

    /// <summary>Oracle Database (via <c>Oracle.EntityFrameworkCore</c>).</summary>
    Oracle,

    /// <summary>SQLite (via <c>Microsoft.EntityFrameworkCore.Sqlite</c>).</summary>
    Sqlite,
}
