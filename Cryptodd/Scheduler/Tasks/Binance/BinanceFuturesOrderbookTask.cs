using System.Diagnostics.CodeAnalysis;
using Cryptodd.Binance.Orderbooks;
using Cryptodd.BinanceFutures.Orderbooks;
using Cryptodd.Bitfinex;
using Lamar;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Scheduler.Tasks.Binance;

// ReSharper disable once UnusedType.Global
public sealed class BinanceFuturesOrderbookTask : BaseBinanceOrderbookTask
{
    public BinanceFuturesOrderbookTask(IContainer container, ILogger logger, IConfiguration configuration) : base(
        container,
        logger, configuration)
    {
        Section = Configuration.GetSection("BinanceFutures:OrderBook:Task");
        OnConfigurationChange();
    }

    [SuppressMessage("ReSharper", "InvertIf")]
    protected override Task<BinanceOrderbookCollectorUnion> GetOrderBookService()
    {
        if (Collector.IsNull)
        {
            lock (LockObject)
            {
                if (Collector.IsNull)
                {
                    Collector =
                        new BinanceOrderbookCollectorUnion(Container.GetInstance<BinanceFuturesOrderbookCollector>());
                }
            }
        }

        return Task.FromResult(Collector);
    }
}