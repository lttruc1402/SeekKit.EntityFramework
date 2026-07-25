namespace SeekKit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// Counts how many database commands (SQL round trips) EF Core executes. Attach via
/// <c>new TestDbContext(interceptor)</c> and call <see cref="Reset"/> after any setup
/// queries (e.g. <c>EnsureCreated</c>/<c>Seed</c>) so the count reflects only the query
/// under test.
/// </summary>
public sealed class CommandCountInterceptor : DbCommandInterceptor
{
    public int Count { get; private set; }

    public void Reset() => Count = 0;

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Count++;
        return base.ReaderExecuting(command, eventData, result);
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Count++;
        return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }
}
