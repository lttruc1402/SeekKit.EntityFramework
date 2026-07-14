using Microsoft.Extensions.DependencyInjection;
using SeekKit.Core.Models;
using SeekKit.MongoDB;
using SeekKit.MongoDB.Core;
using Xunit;

namespace SeekKit.MongoDB.IntegrationTests;

[Collection("mongo")]
public abstract class IntegrationTestBase
{
    protected readonly MongoFixture Fixture;
    protected readonly ISeekMongoService Seek;

    protected IntegrationTestBase(MongoFixture fixture)
    {
        Fixture = fixture;
        var services = new ServiceCollection();
        services.AddSeekKitMongo(o => { o.DefaultPageSize = 10; o.MinPageSize = 1; o.MaxPageSize = 200; });
        Seek = services.BuildServiceProvider().GetRequiredService<ISeekMongoService>();
    }

    /// <summary>
    /// Walks every page forward using <paramref name="fetch"/> (which rebuilds the
    /// source and calls SeekAsync for a given token) and returns the flattened items.
    /// </summary>
    protected static async Task<List<Product>> TraverseForwardAsync(
        Func<string?, Task<SeekResult<Product>>> fetch)
    {
        var all = new List<Product>();
        string? token = null;
        int guard = 0;
        do
        {
            var page = await fetch(token);
            all.AddRange(page.Items);
            token = page.NextToken;
            if (++guard > 1000) throw new Exception("runaway pagination");
        } while (token != null);
        return all;
    }
}
