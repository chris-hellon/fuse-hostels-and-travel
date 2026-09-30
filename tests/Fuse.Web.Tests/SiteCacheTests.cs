using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Shouldly;
using Fuse.Application.Features.Site;

namespace Fuse.Web.Tests;

/// <summary>The public site's cache: production only, and cleared by any command.</summary>
public sealed class SiteCacheTests
{
    private static SiteCache Cache(string environment) =>
        new(new MemoryCache(new MemoryCacheOptions()), new HostingEnvironment { EnvironmentName = environment });

    [Fact]
    public async Task Production_caches_until_cleared()
    {
        var cache = Cache(Environments.Production);
        var loads = 0;
        Task<int> Load() => Task.FromResult(++loads);

        (await cache.GetAsync("key", Load)).ShouldBe(1);
        (await cache.GetAsync("key", Load)).ShouldBe(1);
        cache.Clear();
        (await cache.GetAsync("key", Load)).ShouldBe(2);
    }

    [Fact]
    public async Task Other_environments_always_read_afresh()
    {
        var cache = Cache(Environments.Development);
        var loads = 0;
        Task<int> Load() => Task.FromResult(++loads);

        (await cache.GetAsync("key", Load)).ShouldBe(1);
        (await cache.GetAsync("key", Load)).ShouldBe(2);
    }
}
