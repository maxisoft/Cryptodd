using Cryptodd.IoC;

namespace Cryptodd.IO.FileSystem;

public interface IPathResolver : IService
{
    public string Resolve(string path, in ResolveOption option = default);
}