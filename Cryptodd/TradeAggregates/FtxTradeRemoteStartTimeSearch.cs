using System.Diagnostics;
using Cryptodd.Ftx;

namespace Cryptodd.TradeAggregates;

public class FtxTradeRemoteStartTimeSearch : RemoteStartTimeSearch
{
    private readonly IFtxPublicHttpApi _api;
    private readonly string _market;

    public FtxTradeRemoteStartTimeSearch(IFtxPublicHttpApi api, string market)
    {
        _api = api;
        _market = market;
        Resolution = TimeSpan.FromMinutes(5);
    }

    public override async ValueTask<DateTimeOffset?> ApiCall(DateTimeOffset minimalTime,
        CancellationToken cancellationToken = default)
    {
        using var trades = await _api.GetTradesAsync(_market, minimalTime.ToUnixTimeSeconds(),
            (minimalTime + Resolution).ToUnixTimeSeconds(), cancellationToken).ConfigureAwait(false);
        if (!trades.Any())
        {
            return null;
        }

        var res = trades.Select(static trade => trade.Time).Min();

        var now = DateTimeOffset.UtcNow;
        if (now - MinimumDate > 10 * Resolution && (res - minimalTime).Duration() > 100 * Resolution) // heuristic to detect that remote listing current trade instead of minimalTime ones
        {
            Debug.Assert(false, $"Remote is buggy for query @ {minimalTime}");
            return null;
        }

        return res;
    }
}