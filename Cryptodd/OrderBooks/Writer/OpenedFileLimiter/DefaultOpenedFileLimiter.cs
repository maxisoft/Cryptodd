using Cryptodd.IoC;
using Lamar;
using Maxisoft.Utils.Empties;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.OrderBooks.Writer.OpenedFileLimiter;

[Singleton]
public class DefaultOpenedFileLimiter : OpenedFileLimiter, IService, IDisposable, IDefaultOpenedFileLimiter
{
    public const int DefaultLimit = 128;
    public int Limit { get; internal set; } = DefaultLimit;
    private IDisposable _linkedDisposable;

    public DefaultOpenedFileLimiter(IConfiguration configuration)
    {
        _linkedDisposable = configuration.GetReloadToken().RegisterChangeCallback(ReloadConfig, configuration);
        ReloadConfig(configuration);
    }

    private void ReloadConfig(object? obj)
    {
        if (obj is IConfiguration configuration)
        {
            Limit = configuration.GetSection("FileLimiter").GetValue<int>("Limit", DefaultLimit);
        }
    }

    public bool TryRegister(OpenedFileSource fileSource, out OpenedFileLimiterUnregisterOnDispose res) =>
        TryRegister(fileSource, Limit, out res);

    public async Task Wait(CancellationToken cancellationToken) => await Wait(Limit, cancellationToken);

    public void Dispose()
    {
        _linkedDisposable.Dispose();
        _linkedDisposable = new EmptyDisposable();
        GC.SuppressFinalize(this);
    }
}