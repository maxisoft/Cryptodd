namespace Cryptodd.Ftx.Models;

public record ApiFutureStats(double Volume, float NextFundingRate, string NextFundingTime, double ExpirationPrice,
    double PredictedExpirationPrice, double StrikePrice, double OpenInterest);