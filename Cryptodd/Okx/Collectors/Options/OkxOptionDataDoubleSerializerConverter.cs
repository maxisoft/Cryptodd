using Cryptodd.Mmap;
using Cryptodd.Okx.Collectors.Swap;
using Cryptodd.Okx.Models;
using Cryptodd.Okx.Options;

namespace Cryptodd.Okx.Collectors.Options;

public struct OkxOptionDataDoubleSerializerConverter : IDoubleSerializerConverter<OkxOptionDataContext, OkxOptionData>
{
    public OkxOptionData Convert(in OkxOptionDataContext doubleSerializable)
    {
        var (instrumentId, oi, ticker, option, instrumentInfo) = doubleSerializable;
        var ts = Math.Max(oi.ts, ticker.ts);
        ts = Math.Max(ts, option.ts);
        var price = ticker.last.Value;
        var spreadPercent = ticker.askPx > 0 && ticker.bidPx > 0 ? (ticker.askPx / ticker.bidPx - 1) * 100 : 0;
        if (ticker.askPx > 0 && ticker.bidPx > 0 && spreadPercent < 0.1)
        {
            // use bid and ask midpoint instead of last traded price
            price = (ticker.askPx + ticker.bidPx) * 0.5;
        }

        return new OkxOptionData(
            Timestamp: ts,
            ExpiryTimeDiff: Math.Max(instrumentInfo.expTime - ts, 0),
            Delta: option.delta,
            Gamma: option.gamma,
            Vega: option.vega,
            Theta: option.theta,
            DeltaBs: option.deltaBS,
            GammaBs: option.gammaBS,
            VegaBs: option.vegaBS,
            ThetaBs: option.thetaBS,
            Lever: option.lever,
            MarkVol: option.markVol,
            BidVol: option.bidVol,
            AskVol: option.askVol,
            ForwardPrice: option.fwdPx,
            OpenInterest: oi.oi,
            SpreadPercent: spreadPercent,
            SpreadToMarkPercent: instrumentId.Price > 0 ? (option.fwdPx / instrumentId.Price - 1) * 100 : 0,
            Change24HPercent: ticker.open24h > 0 ? (price / ticker.open24h - 1) * 100 : 0,
            ChangeTodayPercent: ticker.sodUtc0 > 0 ? (price / ticker.sodUtc0 - 1) * 100 : 0,
            ChangeTodayChinaPercent: ticker.sodUtc8 > 0 ? (price / ticker.sodUtc8 - 1) * 100 : 0,
            Volume24H: ticker.vol24h,
            PriceRatio24H: ticker.high24h > ticker.low24h && ticker.low24h > 0
                ? (price - ticker.low24h) / (ticker.high24h - ticker.low24h)
                : 0.5,
            LastPrice: price,
            StrikePrice: instrumentInfo.stk,
            Price: instrumentId.Price
        );
    }
}