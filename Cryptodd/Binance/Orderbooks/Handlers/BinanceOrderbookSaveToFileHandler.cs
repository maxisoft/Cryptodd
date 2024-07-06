using Cryptodd.IoC;

namespace Cryptodd.Binance.Orderbooks.Handlers;

// ReSharper disable once UnusedType.Global
public sealed class BinanceOrderbookSaveToFileHandler(BinanceOrderBookWriter writer)
    : BaseBinanceOrderbookSaveToFileHandler<BinanceOrderBookWriter, BinanceOrderBookWriterOptions>(writer),
        IBinanceAggregatedOrderbookHandler, IService;