using System.Diagnostics.CodeAnalysis;
using Cryptodd.Binance.Orderbook;
using Cryptodd.Bitfinex;
using Lamar;
using Maxisoft.Utils.Collections.Queues.Specialized;
using Microsoft.Extensions.Configuration;
using Polly;
using Serilog;

namespace Cryptodd.Scheduler.Tasks.Binance;

public class BinanceOrderbookTask : BasePeriodicScheduledTask
{
    private AsyncPolicy _retryPolicy;
    private readonly BoundedDeque<CancellationTokenSource> _cancellationTokenSources = new(8);
    private BinanceOrderbookCollector? _collector = null;
    private readonly object lockObject = new object();

    public BinanceOrderbookTask(IContainer container, ILogger logger, IConfiguration configuration) : base(
        logger,
        configuration, container)
    {
        _retryPolicy = Policy.NoOpAsync();
        Period = TimeSpan.FromSeconds(15);
        ConfigureRetryPolicy();
    }

    public override IConfigurationSection Section =>
        Configuration.GetSection("Binance:OrderBook:Task");

    public override async Task Execute(CancellationToken cancellationToken)
    {
        var cts = CreateCancellationTokenSource(cancellationToken);
        var orderBookService = await GetOrderBookService();
        await _retryPolicy.ExecuteAsync(_ => orderBookService.CollectOrderBook(cts.Token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    [SuppressMessage("ReSharper", "InvertIf")]
    protected virtual async Task<BinanceOrderbookCollector> GetOrderBookService()
    {
        if (_collector is null)
        {
            lock (lockObject)
            {
                if (_collector is null)
                {
                    _collector = Container.GetInstance<BinanceOrderbookCollector>();
                }
            }
        }

        return _collector;
    }

    protected virtual CancellationTokenSource CreateCancellationTokenSource(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            cts.CancelAfter(Period);
            while (_cancellationTokenSources.IsFull)
            {
                if (_cancellationTokenSources.TryPopFront(out var old))
                {
                    old.Dispose();
                }
            }

            var stable = false;
            while (!stable)
            {
                stable = true;
                foreach (var source in _cancellationTokenSources)
                {
                    try
                    {
                        source.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        _cancellationTokenSources.Remove(source);
                        stable = false;
                        break;
                    }
                }
            }


            _cancellationTokenSources.Add(cts);
            return cts;
        }
        catch
        {
            cts.Dispose();
            throw;
        }
    }

    private void ConfigureRetryPolicy()
    {
        var maxRetry = Section.GetValue("MaxRetry", 3);
        _retryPolicy = Policy.Handle<Exception>(_ => true)
            .WaitAndRetryAsync(maxRetry, i => TimeSpan.FromSeconds(1 + i));
    }

    protected override void OnConfigurationChange(object obj)
    {
        base.OnConfigurationChange(obj);
        ConfigureRetryPolicy();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var cts in _cancellationTokenSources)
            {
                cts.Dispose();
            }

            _cancellationTokenSources.Clear();
        }

        base.Dispose(disposing);
    }
}