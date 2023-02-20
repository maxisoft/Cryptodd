namespace Cryptodd.IO.FileSystem;

public interface IPluginPathResolver : IPathResolver
{
    public int Priority { get; }
}