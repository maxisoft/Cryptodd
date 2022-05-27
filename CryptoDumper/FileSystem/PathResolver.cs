using CryptoDumper.IoC;
using Microsoft.Extensions.Configuration;

namespace CryptoDumper.FileSystem;

public interface IPathResolver : IService
{
    public string Resolve(string path);
}

public class PathResolver : IPathResolver
{
    private readonly IConfiguration _configuration;

    public PathResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Resolve(string path)
    {
        if (File.Exists(path))
        {
            return path;
        }

        var basePath = _configuration.GetValue<string>("BasePath");
        return Path.Combine(basePath, path);
    }
}