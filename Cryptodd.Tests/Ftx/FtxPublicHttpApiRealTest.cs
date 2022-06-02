using System;
using System.Linq;
using System.Net.Http;
using Cryptodd.Ftx;
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
        var res = await new FtxPublicHttpApi(httpclient, new UriRewriteService()).GetAllFuturesAsync();
        Assert.NotEmpty(res);
        Assert.NotNull(res.FirstOrDefault(future =>
            future.Name.Equals("btc-perp", StringComparison.OrdinalIgnoreCase)));
    }

    [RetryFact]
    public async void TestGetAllFundingRatesAsync()
    {
        using var httpclient = new HttpClient();
        var res = await new FtxPublicHttpApi(httpclient, new UriRewriteService()).GetAllFundingRatesAsync();
        Assert.NotEmpty(res);
        Assert.True(res.Exists(future => future.Future.Equals("btc-perp", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async void TestGetAllMarketsAsync()
    {
        using var httpclient = new HttpClient();
        var res = await new FtxPublicHttpApi(httpclient, new UriRewriteService()).GetAllMarketsAsync();
        Assert.NotEmpty(res);
        Assert.True(res.Exists(market => market.Name.Equals("btc-perp", StringComparison.OrdinalIgnoreCase)));
    }
}