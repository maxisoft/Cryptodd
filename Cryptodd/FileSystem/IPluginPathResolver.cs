namespace Cryptodd.FileSystem;

public interface IPluginPathResolver : IPathResolver
{
    public int Priority { get; }
}