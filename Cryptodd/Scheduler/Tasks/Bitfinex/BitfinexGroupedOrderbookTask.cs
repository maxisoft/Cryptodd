using System.Diagnostics;
using Cryptodd.Bitfinex;
using Cryptodd.Ftx.Orderbooks;
using Lamar;
using Microsoft.Extensions.Configuration;
using Polly;
using Serilog;

namespace Cryptodd.Scheduler.Tasks.Ftx;

public class BitfinexGroupedOrderbookTask : BasePeriodicScheduledTask
{
    private AsyncPolicy _retryPolicy;

    public BitfinexGroupedOrderbookTask(IContainer container, ILogger logger, IConfiguration configuration) : base(logger,
        configuration, container)
    {
        _retryPolicy = Policy.NoOpAsync();
        ConfigureRetryPolicy();
    }

    public override IConfigurationSection Section =>
        Configuration.GetSection("Bitfinex").GetSection("OrderBook").GetSection("Task");

    public override Task Execute(CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        return _retryPolicy.ExecuteAsync(token =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, token);
            cts.CancelAfter(Period - sw.Elapsed);
            var orderBookService = Container.GetInstance<BitfinexGatherGroupedOrderBookService>();
            return orderBookService.CollectOrderBooks(cts.Token);
        }, cancellationToken);
    }

    private void ConfigureRetryPolicy()
    {
        var maxRetry = Section.GetValue("MaxRetry", 3);
        _retryPolicy = Policy.TimeoutAsync(TimeSpan.FromMilliseconds(Period.TotalMilliseconds / maxRetry))
            .WrapAsync(
                Policy.Handle<Exception>(_ => true).WaitAndRetryAsync(maxRetry, i => TimeSpan.FromSeconds(1 + i)));
    }

    protected override void OnConfigurationChange(object obj)
    {
        base.OnConfigurationChange(obj);
        ConfigureRetryPolicy();
    }
}