namespace Cryptodd.Plugins;

public interface IBasePlugin : IAsyncDisposable
{
    string Name { get; }
    Version Version { get; }
    int Order { get; }
    Task OnStart();
}