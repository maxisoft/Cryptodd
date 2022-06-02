using System;

namespace Cryptodd.Plugins
{
    public interface IBasePlugin : IAsyncDisposable
    {
        string Name { get; }
        Version Version { get; }
        Task OnStart();
        int Order { get; }
    }
}