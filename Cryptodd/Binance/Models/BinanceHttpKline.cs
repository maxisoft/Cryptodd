namespace Cryptodd.Binance.Models;

public readonly record struct BinanceHttpKline(
    long OpenTime,
    double Open,
    double High,
    double Low,
    double Close,
    double Volume,
    long CloseTime,
    double QuoteAssetVolume,
    int NumberOfTrades,
    double TakerBuyBaseAssetVolume,
    double TakerBuyQuoteAssetVolume
);