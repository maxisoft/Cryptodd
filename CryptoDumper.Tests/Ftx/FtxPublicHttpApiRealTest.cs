using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using CryptoDumper.Ftx;
using CryptoDumper.Http;
using Xunit;

namespace CryptoDumper.Tests.Ftx;

public class FtxPublicHttpApiRealTest
{
    [Fact]
    public async void TestGetAllFuturesAsync()
    {
        using var httpclient = new HttpClient();
        var res = await (new FtxPublicHttpApi(httpclient, new UriRewriteService())).GetAllFuturesAsync();
        Assert.NotEmpty(res);
        Assert.NotNull(res.FirstOrDefault(future => future.Name.Equals("btc-perp", StringComparison.OrdinalIgnoreCase)));
    }
    
    [Fact]
    public async void TestGetAllFundingRatesAsync()
    {
        using var httpclient = new HttpClient();
        var res = await (new FtxPublicHttpApi(httpclient, new UriRewriteService())).GetAllFundingRatesAsync();
        Assert.NotEmpty(res);
        Assert.True(res.Exists(future => future.Future.Equals("btc-perp", StringComparison.OrdinalIgnoreCase)));
    }
    
    [Fact]
    public async void TestGetAllMarketsAsync()
    {
        using var httpclient = new HttpClient();
        var res = await (new FtxPublicHttpApi(httpclient, new UriRewriteService())).GetAllMarketsAsync();
        Assert.NotEmpty(res);
        Assert.True(res.Exists(market => market.Name.Equals("btc-perp", StringComparison.OrdinalIgnoreCase)));
    }
}