namespace SeekKit.EntityFramework.Tests.Infrastructure;

/// <summary>
/// xUnit class fixture that wires up a DI container with SeekKit registered for
/// SQLite.
///
/// Shared across all integration test classes via IClassFixture&lt;SeekFixture&gt;.
/// </summary>
public sealed class SeekFixture
{
    public ISeekFactory Factory  { get; }
    public ISeekService  Service  { get; }

    public SeekFixture()
    {
        var services = new ServiceCollection();

        services.AddSeekKit(opt =>
        {
            opt.Strategy        = DatabaseStrategy.ForSqlite();
            opt.DefaultPageSize = 10;
            opt.MinPageSize     = 1;
            opt.MaxPageSize     = 100;
        });

        var provider = services.BuildServiceProvider();

        Factory = provider.GetRequiredService<ISeekFactory>();
        Service = provider.GetRequiredService<ISeekService>();
    }
}
