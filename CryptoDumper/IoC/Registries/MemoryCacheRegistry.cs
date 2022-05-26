using Lamar;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace CryptoDumper.IoC.Registries;

public class MemoryCacheRegistry : ServiceRegistry
{
    public MemoryCacheRegistry()
    {
        ForSingletonOf<MemoryCache>().Use(context => new MemoryCache(context.GetInstance<IConfiguration>().GetSection("Cache").Get<MemoryCacheOptions>() ?? new MemoryCacheOptions()));
        For<IMemoryCache>().Use(context => context.GetInstance<MemoryCache>());
    }
}