using Cryptodd.Binance.Orderbooks.Handlers;
using Cryptodd.IoC;

namespace Cryptodd.BinanceFutures.Orderbooks.Handlers;

// ReSharper disable once UnusedType.Global
public sealed class BinanceFuturesOrderbookSaveToFileHandler :
    BaseBinanceOrderbookSaveToFileHandler<BinanceFuturesOrderBookWriter, BinanceFuturesOrderBookWriterOptions>,
    IBinanceFuturesAggregatedOrderbookHandler, IService
{
    public BinanceFuturesOrderbookSaveToFileHandler(BinanceFuturesOrderBookWriter writer) : base(writer) { }
}