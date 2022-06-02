using CryptoDumper.IoC;

namespace CryptoDumper.FileSystem;

public interface IPathResolver : IService
{
    public string Resolve(string path);
}