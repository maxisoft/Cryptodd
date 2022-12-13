using System.Diagnostics;
using Cryptodd.IoC;
using Lamar;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Bitfinex;

public interface IBitfinexCachedPairProvider : IBitfinexPairProvider
{
    
}

[Singleton]
public class BitfinexCachedPairProvider : IBitfinexCachedPairProvider, IService, IDisposable
{
    private IBitfinexPublicHttpApi _http;
    private TimeSpan _cacheDuration;
    private Stopwatch? _stopwatch;
    private ArrayList<string> _cached = new();
    private readonly CancellationTokenSource _cancellationTokenSource;
    private Task<ArrayList<string>>? _task;
    private readonly ILogger _logger;
    
    public BitfinexCachedPairProvider(IBitfinexPublicHttpApi http, IConfiguration configuration, Boxed<CancellationToken> cancellationToken, ILogger logger)
    {
        _logger = logger.ForContext(GetType());
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _http = http;
        _cacheDuration = configuration.GetSection("BitfinexCachedPairProvider")
            .GetValue("CacheDuration", TimeSpan.FromMinutes(1));
    }

    public async ValueTask<ArrayList<string>> GetAllPairs(CancellationToken cancellationToken)
    {
        async Task<ArrayList<string>> BackgroundTask()
        {
            using var cts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);
            cts.CancelAfter(_cacheDuration);
            var res = await _http.GetAllPairs(cts.Token).ConfigureAwait(false);
            _cached = res;
            _stopwatch = Stopwatch.StartNew();
            return res;
        }
        
        if (_stopwatch is null || _stopwatch.Elapsed > _cacheDuration)
        {
            switch (_task)
            {
                case null:
                    _task = BackgroundTask();
                    break;
                case {IsFaulted: true}:
                    _logger.Error(_task.Exception, "{Name} faulted", nameof(BackgroundTask));
                    _task.Dispose();
                    _task = BackgroundTask();
                    break;
                case {IsCompleted: true}:
                    _task.Dispose();
                    _task = BackgroundTask();
                    break;
            }
        }

        if (_cached.Count > 0 && _stopwatch?.Elapsed < 2 * _cacheDuration)
        {
            return _cached;
        }

        if (_task is null or { IsCompleted: true })
        {
            _task?.Dispose();
            _task = BackgroundTask();
        }

        return await _task.ConfigureAwait(false);
    }

    public void Dispose()
    {
        _task?.Dispose();
        _task = null;
        try
        {
            _cancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        
        _cancellationTokenSource.Dispose();
        GC.SuppressFinalize(this);
    }
}