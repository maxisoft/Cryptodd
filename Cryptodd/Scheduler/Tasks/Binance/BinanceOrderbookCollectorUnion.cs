using System.Diagnostics;
using Cryptodd.Binance.Orderbooks;
using Cryptodd.BinanceFutures.Orderbooks;

namespace Cryptodd.Scheduler.Tasks.Binance;

public readonly struct BinanceOrderbookCollectorUnion
{
    private readonly BinanceOrderbookCollector? _spot;
    private readonly BinanceFuturesOrderbookCollector? _futures;

    public BinanceOrderbookCollectorUnion() { }

    public BinanceOrderbookCollectorUnion(BinanceOrderbookCollector? spot) : this()
    {
        Debug.Assert(_futures is null);
        _spot = spot;
    }

    public BinanceOrderbookCollectorUnion(BinanceFuturesOrderbookCollector? futures) : this()
    {
        Debug.Assert(_spot is null);
        _futures = futures;
    }

    public Task CollectOrderBook(CancellationToken cancellationToken)
    {
        // ReSharper disable once ArrangeMethodOrOperatorBody
        return _spot?.CollectOrderBook(cancellationToken) ??
               _futures?.CollectOrderBook(cancellationToken) ??
               Task.CompletedTask;
    }

    public bool IsNull => _futures is null && _spot is null;
}