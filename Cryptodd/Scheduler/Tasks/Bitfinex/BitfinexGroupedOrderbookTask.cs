using System.Diagnostics;
using Cryptodd.Bitfinex;
using Cryptodd.Bitfinex.Models;
using Cryptodd.Ftx.Orderbooks;
using Lamar;
using Maxisoft.Utils.Collections.Queues;
using Maxisoft.Utils.Collections.Queues.Specialized;
using Microsoft.Extensions.Configuration;
using Polly;
using Serilog;

namespace Cryptodd.Scheduler.Tasks.Bitfinex;

public class BitfinexGroupedOrderbookTask : BasePeriodicScheduledTask
{
    private AsyncPolicy _retryPolicy;
    private readonly BoundedDeque<CancellationTokenSource> _cancellationTokenSources = new(8);

    public BitfinexGroupedOrderbookTask(IContainer container, ILogger logger, IConfiguration configuration) : base(
        logger,
        configuration, container)
    {
        _retryPolicy = Policy.NoOpAsync();
        Period = TimeSpan.FromSeconds(15);
        Section = Configuration.GetSection("Bitfinex:OrderBook:Task");
        OnConfigurationChange();
        ConfigureRetryPolicy();
    }

    public override async Task Execute(CancellationToken cancellationToken)
    {
        var cts = CreateCancellationTokenSource(cancellationToken);
        var orderBookService = GetOrderBookService();
        await _retryPolicy.ExecuteAsync(_ =>
            {
                void OrderbookContinuation(List<OrderbookEnvelope>? obs)
                {
                    if (obs?.Count is null or 0)
                    {
                        return;
                    }

                    var ctx = TaskRunningContext;
                    if (ctx is null)
                    {
                        return;
                    }

                    ctx.Hooks.TimeElapsed = new Lazy<TimeSpan?>(ctx.TimeElapsed);
                }

                return orderBookService.CollectOrderBooks(OrderbookContinuation, Period * 0.95, cts.Token);
            },
            cancellationToken);
        cts.Cancel();
    }

    protected virtual BitfinexGatherGroupedOrderBookService GetOrderBookService() =>
        Container.GetInstance<BitfinexGatherGroupedOrderBookService>();

    protected virtual CancellationTokenSource CreateCancellationTokenSource(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            cts.CancelAfter(Period * 1.5);
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
        _retryPolicy = Policy.Handle<Exception>(static e => e is not OperationCanceledException)
            .WaitAndRetryAsync(maxRetry, i => TimeSpan.FromSeconds(1 + i));
    }

    protected override void OnConfigurationChange(object? obj)
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