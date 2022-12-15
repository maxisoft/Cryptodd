using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using Cryptodd.Binance.Http;
using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.BinanceFutures.Http;
using Cryptodd.BinanceFutures.Http.RateLimiter;
using Cryptodd.Http;
using Microsoft.Extensions.Configuration;
using xRetry;
using Xunit;

namespace Cryptodd.Tests.BinanceFutures.Http;

public class BinancePublicHttpApiRealTest
{
    [RetryFact]
    public async void TestGetExchangeInfoAsync()
    {
        using var httpclient = new HttpClient();
        var config = new ConfigurationBuilder().AddInMemoryCollection(Array.Empty<KeyValuePair<string, string?>>()).Build();
        var res = await new BinanceFuturesPublicHttpApi(httpclient, config, new UriRewriteService(), new EmptyBinanceFuturesRateLimiter()).GetExchangeInfoAsync();
        Assert.NotEmpty(res);
        Assert.NotEmpty(res["symbols"] as JsonArray ?? new JsonArray());
    }
    
    [RetryFact]
    public async void TestGetOrderbook()
    {
        using var httpclient = new HttpClient();
        var config = new ConfigurationBuilder().AddInMemoryCollection(Array.Empty<KeyValuePair<string, string?>>()).Build();
        var res = await new BinanceFuturesPublicHttpApi(httpclient, config, new UriRewriteService(), new EmptyBinanceFuturesRateLimiter()).GetOrderbook("ETHUSDT");
        Assert.NotEmpty(res.Asks);
        Assert.NotEmpty(res.Bids);
    }
}