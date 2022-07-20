namespace Cryptodd.Bitfinex.Models;

public readonly record struct DerivativeStatus(string Key, long Time, double DerivativeMidPrice, double SpotPrice,
    double InsuranceFundBalance, long NextFundingEvtTimestampMs, double NextFundingAccrued, int NextFundingStep,
    double CurrentFunding, double MarkPrice, double OpenInterest, double ClampMin, double ClampMax);