using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using Cryptodd.Binance;
using Cryptodd.Binance.RateLimiter;
using Cryptodd.Http;
using Microsoft.Extensions.Configuration;
using xRetry;
using Xunit;

namespace Cryptodd.Tests.Binance;

public class BinancePublicHttpApiRealTest
{
    [RetryFact]
    public async void TestGetExchangeInfoAsync()
    {
        using var httpclient = new HttpClient();
        var config = new ConfigurationBuilder().AddInMemoryCollection(Array.Empty<KeyValuePair<string, string?>>()).Build();
        var res = await new BinancePublicHttpApi(httpclient, config, new UriRewriteService(), new EmptyBinanceRateLimiter()).GetExchangeInfoAsync();
        Assert.NotEmpty(res);
        Assert.NotEmpty(res["symbols"] as JsonArray ?? new JsonArray());
    }
    
    [RetryFact]
    public async void TestGetOrderbook()
    {
        using var httpclient = new HttpClient();
        var config = new ConfigurationBuilder().AddInMemoryCollection(Array.Empty<KeyValuePair<string, string?>>()).Build();
        var res = await new BinancePublicHttpApi(httpclient, config, new UriRewriteService(), new EmptyBinanceRateLimiter()).GetOrderbook("ETHBTC");
        Assert.NotEmpty(res.Asks);
        Assert.NotEmpty(res.Bids);
    }
}