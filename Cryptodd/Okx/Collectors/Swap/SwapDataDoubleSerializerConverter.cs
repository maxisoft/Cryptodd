using Cryptodd.IO;
using Cryptodd.IO.Mmap;
using Cryptodd.Okx.Models;

namespace Cryptodd.Okx.Collectors.Swap;

public struct SwapDataDoubleSerializerConverter : IDoubleSerializerConverter<
    (OkxHttpOpenInterest, OkxHttpFundingRate, OkxHttpTickerInfo, OkxHttpMarkPrice), SwapData>
{
    public SwapData Convert(
        in (OkxHttpOpenInterest, OkxHttpFundingRate, OkxHttpTickerInfo, OkxHttpMarkPrice) doubleSerializable)
    {
        var (oi, fr, ticker, markPrice) = doubleSerializable;
        var ts = Math.Max(oi.ts, fr.ts);
        var price = ticker.last.Value;
        var spreadPercent = ticker.askPx > 0 && ticker.bidPx > 0 ? (ticker.askPx / ticker.bidPx - 1) * 100 : 0;
        if (ticker.askPx > 0 && ticker.bidPx > 0 && spreadPercent < 0.05)
        {
            // use bid and ask midpoint instead of last traded price
            price = (ticker.askPx + ticker.bidPx) * 0.5;
        }

        double nextFundingRate = fr.nextFundingRate;
        if (!double.IsNormal(nextFundingRate) || fr.method == "current_period")
        {
            if (fr.settState == "processing" && double.IsNormal(fr.settFundingRate))
            {
                nextFundingRate = fr.settFundingRate;
            }
            else
            {
                nextFundingRate = fr.fundingRate;
            }
        }

        return new SwapData(
            Timestamp: ts,
            NextFundingTime: fr.nextFundingTime - ts,
            FundingRate: fr.fundingRate,
            NextFundingRate: nextFundingRate,
            OpenInterest: oi.oi,
            OpenInterestInCurrency: oi.oiCcy,
            SpreadPercent: spreadPercent,
            SpreadToMarkPercent: markPrice.markPx > 0 && price > 0 ? (markPrice.markPx / price - 1) * 100 : 0,
            AskSize: ticker.askSz,
            BidSize: ticker.bidSz,
            Change24HPercent: ticker.open24h > 0 ? (price / ticker.open24h - 1) * 100 : 0,
            ChangeTodayPercent: ticker.sodUtc0 > 0 ? (price / ticker.sodUtc0 - 1) * 100 : 0,
            ChangeTodayChinaPercent: ticker.sodUtc8 > 0 ? (price / ticker.sodUtc8 - 1) * 100 : 0,
            Volume24H: ticker.vol24h,
            PriceRatio24H: ticker.high24h > ticker.low24h && ticker.low24h > 0
                ? (price - ticker.low24h) / (ticker.high24h - ticker.low24h)
                : 0.5,
            LastPrice: price
        );
    }
}