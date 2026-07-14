using SeekKit.Core;
using SeekKit.EntityFramework;
using SeekKit.EntityFramework.Core;

namespace SeekKit.MongoDB.Tests;

/// <summary>
/// An application can register both SeekKit.EntityFramework and
/// SeekKit.MongoDB. These tests pin the contract: one shared converter
/// registry and serializer, provider-specific converters present, and both
/// services functional — regardless of registration order.
/// </summary>
public sealed class DualProviderRegistrationTests
{
    private sealed class Doc
    {
        public ObjectId Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private static ServiceProvider BuildProvider(bool mongoFirst)
    {
        var services = new ServiceCollection();

        if (mongoFirst)
        {
            services.AddSeekKitMongo(o => o.DefaultPageSize = 10);
            services.AddSeekKit(o => o.Strategy = DatabaseStrategy.ForSqlServer());
        }
        else
        {
            services.AddSeekKit(o => o.Strategy = DatabaseStrategy.ForSqlServer());
            services.AddSeekKitMongo(o => o.DefaultPageSize = 10);
        }

        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothOrders_SingleSharedRegistry_WithObjectIdConverter(bool mongoFirst)
    {
        using var provider = BuildProvider(mongoFirst);

        var registries = provider.GetServices<ITypeConverterRegistry>().ToList();

        Assert.Single(registries);
        Assert.True(registries[0].HasConverter(typeof(ObjectId)));
        Assert.True(registries[0].HasConverter(typeof(int)));   // defaults present too
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothOrders_SingleSerializer_AndBothServicesResolve(bool mongoFirst)
    {
        using var provider = BuildProvider(mongoFirst);

        Assert.Single(provider.GetServices<ISeekSerializer>());
        Assert.NotNull(provider.GetRequiredService<ISeekMongoService>());
        Assert.NotNull(provider.GetRequiredService<SeekKit.EntityFramework.Core.ISeekService>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothOrders_MongoPaginationWithObjectId_Works(bool mongoFirst)
    {
        // Before the shared AddSeekKitCore fix, registering AddSeekKit *after*
        // AddSeekKitMongo replaced the registry with one lacking the ObjectId
        // converter, and this scenario threw at query time.
        using var provider = BuildProvider(mongoFirst);
        var seek = provider.GetRequiredService<ISeekMongoService>();

        var docs = Enumerable.Range(1, 25)
            .Select(i => new Doc { Id = new ObjectId($"{i:x24}"), Name = $"Doc {i}" })
            .ToList();

        var page1 = await seek.SeekAsync(docs.AsQueryable(), new SeekRequest(), b => b.OrderBy(d => d.Id));
        var page2 = await seek.SeekAsync(docs.AsQueryable(), new SeekRequest { Token = page1.NextToken }, b => b.OrderBy(d => d.Id));

        Assert.Equal(10, page1.Count);
        Assert.Equal("Doc 11", page2.Items[0].Name);
    }

    [Fact]
    public void CustomConverter_RegisteredViaEf_VisibleToSharedRegistry()
    {
        var services = new ServiceCollection();
        services.AddSeekKitMongo();
        services.AddSeekKit(
            o => o.Strategy = DatabaseStrategy.ForSqlServer(),
            cfg => cfg.AddConverter<Version>(v => v.ToString(), s => Version.Parse(s!)));

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<ITypeConverterRegistry>();

        Assert.True(registry.HasConverter(typeof(Version)));
        Assert.True(registry.HasConverter(typeof(ObjectId)));
    }
}
