using Cryptodd.IoC;

namespace Cryptodd.Binance.Orderbooks.Handlers;

// ReSharper disable once UnusedType.Global
public sealed class BinanceOrderbookSaveToFileHandler : BaseBinanceOrderbookSaveToFileHandler<BinanceOrderBookWriter, BinanceOrderBookWriterOptions>, IBinanceAggregatedOrderbookHandler, IService
{
    public BinanceOrderbookSaveToFileHandler(BinanceOrderBookWriter writer) : base(writer) { }
}