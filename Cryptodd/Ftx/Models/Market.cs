namespace Cryptodd.Ftx.Models;

public record Market(string Name, string BaseCurrency, string QuoteCurrency, double? QuoteVolume24H,
    double? Change1H, double? Change24H, double? ChangeBod, bool HighLeverageFeeExempt, double? MinProvideSize,
    string Type, string? Underlying, bool Enabled, double? Ask, double? Bid, double? Last, bool PostOnly, double? Price,
    double PriceIncrement, double SizeIncrement, bool Restricted, double? VolumeUsd24H, double? LargeOrderThreshold, bool IsEtfMarket);