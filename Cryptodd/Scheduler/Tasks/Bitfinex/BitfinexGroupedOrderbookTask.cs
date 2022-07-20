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

    public BitfinexGroupedOrderbookTask(IContainer container, ILogger logger, IConfiguration configuration) : base(
        logger,
        configuration, container)
    {
        _retryPolicy = Policy.NoOpAsync();
        ConfigureRetryPolicy();
        Period = TimeSpan.FromMinutes(1);
    }

    public override IConfigurationSection Section =>
        Configuration.GetSection("Bitfinex").GetSection("OrderBook").GetSection("Task");

    public override async Task Execute(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(Period);
        var orderBookService = Container.GetInstance<BitfinexGatherGroupedOrderBookService>();
        await orderBookService.CollectOrderBooks(cts.Token).ConfigureAwait(false);
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
}