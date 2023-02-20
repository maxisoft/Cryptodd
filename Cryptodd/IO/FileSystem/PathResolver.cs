using System.Collections.Concurrent;
using Lamar;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.IO.FileSystem;

[Singleton]
// ReSharper disable once UnusedType.Global
public class PathResolver : IPathResolver
{
    private readonly IConfiguration _configuration;

    private readonly ConcurrentDictionary<(string, ResolveOption), string> _cache = new();

    private readonly IContainer _container;

    public PathResolver(IConfiguration configuration, IContainer container)
    {
        _configuration = configuration;
        _container = container;
    }

    public string Resolve(string path, in ResolveOption option = default)
    {
        // ReSharper disable once LoopCanBeConvertedToQuery
        foreach (var pluginPathResolver in _container.GetAllInstances<IPluginPathResolver>().OrderBy(resolver => resolver.Priority))
        {
            path = pluginPathResolver.Resolve(path, in option);
        }
        
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