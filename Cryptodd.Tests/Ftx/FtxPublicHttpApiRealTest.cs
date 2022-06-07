using System;
using System.Linq;
using System.Net.Http;
using Cryptodd.Ftx;
using Cryptodd.Ftx.Models;
using Cryptodd.Http;
using xRetry;
using Xunit;

namespace Cryptodd.Tests.Ftx;

public class FtxPublicHttpApiRealTest
{
    [RetryFact]
    public async void TestGetAllFuturesAsync()
    {
        using var httpclient = new HttpClient();
        using var res = await new FtxPublicHttpApi(httpclient, new UriRewriteService()).GetAllFuturesAsync();
        Assert.NotEmpty(res);
        Assert.NotNull(res.FirstOrDefault(future =>
            future.Name.Equals("btc-perp", StringComparison.OrdinalIgnoreCase)));
        Assert.True(res.Capacity == FtxPublicHttpApi.FutureDefaultCapacity); // test doesn't reallocate
    }

    [RetryFact]
    public async void TestGetAllFundingRatesAsync()
    {
        using var httpclient = new HttpClient();
        var res = await new FtxPublicHttpApi(httpclient, new UriRewriteService()).GetAllFundingRatesAsync();
        Assert.NotEmpty(res);
        Assert.True(res.Exists(future => future.Future.Equals("btc-perp", StringComparison.OrdinalIgnoreCase)));
    }

    [RetryFact]
    public async void TestGetAllMarketsAsync()
    {
        using var httpclient = new HttpClient();
        using var res = await new FtxPublicHttpApi(httpclient, new UriRewriteService()).GetAllMarketsAsync();
        Assert.NotEmpty(res);
        Assert.True(res.Exists(market => market.Name.Equals("btc-perp", StringComparison.OrdinalIgnoreCase)));
        Assert.True(res.Capacity == FtxPublicHttpApi.MarketDefaultCapacity); // test doesn't reallocate
    }
    
    [RetryFact]
    public async void TestGetTradesAsync()
    {
        using var httpclient = new HttpClient();
        using var res = await new FtxPublicHttpApi(httpclient, new UriRewriteService()).GetTradesAsync("BTC-PERP");
        Assert.NotEmpty(res);
        Assert.Empty(res.Where(trade => trade.Flag == FtxTradeFlag.None));
        Assert.Empty(res.Where(trade => trade.IsBuy & trade.IsSell));
        Assert.True(res.Capacity == FtxPublicHttpApi.TradeDefaultCapacity); // test doesn't reallocate
    }
}