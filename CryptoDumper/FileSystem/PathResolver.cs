using CryptoDumper.IoC;
using Microsoft.Extensions.Configuration;

namespace CryptoDumper.FileSystem;

public interface IPathResolver : IService
{
    public string Resolve(string path);
}

public class PathResolver
{
    private readonly IConfiguration _configuration;

    public PathResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string Resolve(string path)
    {
        return path;
    }
}