using Cryptodd.IoC;

namespace Cryptodd.FileSystem;

public interface IPathResolver : IService
{
    public string Resolve(string path);
}