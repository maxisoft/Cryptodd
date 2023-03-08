using Lamar;
using Maxisoft.Utils.Collections.Queues.Specialized;
using Microsoft.Extensions.Configuration;
using Polly;
using Serilog;

namespace Cryptodd.Scheduler.Tasks.Binance;

public abstract class BaseBinanceOrderbookTask : BasePeriodicScheduledTaskWithRetryPolicyAndCancellationTokenSources
{
    protected BinanceOrderbookCollectorUnion Collector { get; set; } = new();
    protected object LockObject { get; } = new();

    public BaseBinanceOrderbookTask(IContainer container, ILogger logger, IConfiguration configuration) : base(
        logger,
        configuration, container)
    {
        Period = TimeSpan.FromSeconds(15);
    }

    public override async Task Execute(CancellationToken cancellationToken)
    {
        var cts = CreateCancellationTokenSource(cancellationToken);
        var orderBookService = await GetOrderBookService();
        await RetryPolicy.ExecuteAsync(_ => orderBookService.CollectOrderBook(cts.Token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    protected abstract Task<BinanceOrderbookCollectorUnion> GetOrderBookService();

    private void ConfigureRetryPolicy()
    {
        var maxRetry = Section.GetValue("MaxRetry", 3);
        RetryPolicy = Policy.Handle<Exception>(static e => e is not OperationCanceledException)
            .WaitAndRetryAsync(maxRetry, i => TimeSpan.FromSeconds(1 + i));
    }

    protected override void OnConfigurationChange(object? obj)
    {
        base.OnConfigurationChange(obj);
        ConfigureRetryPolicy();
    }
}