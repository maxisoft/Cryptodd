using System.Collections.Concurrent;
using Lamar;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.FileSystem;

[Singleton]
public class PathResolver : IPathResolver
{
    private readonly IConfiguration _configuration;

    private readonly ConcurrentDictionary<(string, ResolveOption), string> _cache =
        new ConcurrentDictionary<(string, ResolveOption), string>();

    public PathResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Resolve(string path, in ResolveOption option = default)
    {
        if (option.AllowCache && _cache.TryGetValue((path, option), out var result))
        {
            return result;
        }

        string SaveToCache(string result, in ResolveOption option)
        {
            if (option.AllowCache)
            {
                _cache.TryAdd((result, option), result);
            }

            return result;
        }
        
        if (File.Exists(path))
        {
            return SaveToCache(path, in option);
        }

        if (!option.Resolve)
        {
            return SaveToCache(path, in option);
        }

        var basePath = _configuration.GetValue<string>("BasePath");
        return SaveToCache(Path.Combine(basePath, path), in option);
    }
}