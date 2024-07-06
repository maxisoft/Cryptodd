using Cryptodd.Binance.Orderbooks.Handlers;
using Cryptodd.IoC;

namespace Cryptodd.BinanceFutures.Orderbooks.Handlers;

// ReSharper disable once UnusedType.Global
public sealed class BinanceFuturesOrderbookSaveToFileHandler(BinanceFuturesOrderBookWriter writer) :
    BaseBinanceOrderbookSaveToFileHandler<BinanceFuturesOrderBookWriter, BinanceFuturesOrderBookWriterOptions>(writer),
    IBinanceFuturesAggregatedOrderbookHandler, IService;