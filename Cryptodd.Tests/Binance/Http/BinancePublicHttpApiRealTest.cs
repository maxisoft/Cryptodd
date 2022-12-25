using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Nodes;
using Cryptodd.Binance;
using Cryptodd.Binance.Http;
using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using Serilog;
using Serilog.Core;
using xRetry;
using Xunit;

namespace Cryptodd.Tests.Binance.Http;

public class BinancePublicHttpApiRealTest
{
    [RetryFact]
    public async void TestGetExchangeInfoAsync()
    {
        using var httpclient = new HttpClient();
        var config = new ConfigurationBuilder().AddInMemoryCollection(Array.Empty<KeyValuePair<string, string?>>()).Build();
        var res = await new BinancePublicHttpApi(httpclient, new Mock<Logger>(MockBehavior.Loose){CallBase = true}.Object, config, new UriRewriteService(), new EmptyBinanceRateLimiter()).GetExchangeInfoAsync();
        Assert.NotEmpty(res);
        Assert.NotEmpty(res["symbols"] as JsonArray ?? new JsonArray());
    }
    
    [RetryFact]
    public async void TestGetOrderbook()
    {
        using var httpclient = new HttpClient();
        var config = new ConfigurationBuilder().AddInMemoryCollection(Array.Empty<KeyValuePair<string, string?>>()).Build();
        var res = await new BinancePublicHttpApi(httpclient, new Mock<Logger>(MockBehavior.Loose){CallBase = true}.Object, config, new UriRewriteService(), new EmptyBinanceRateLimiter()).GetOrderbook("ETHBTC");
        Assert.NotEmpty(res.Asks);
        Assert.NotEmpty(res.Bids);
    }
}