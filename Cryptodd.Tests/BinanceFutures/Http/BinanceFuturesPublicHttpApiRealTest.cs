using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using Cryptodd.Binance.Http;
using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.Binance.Models;
using Cryptodd.BinanceFutures.Http;
using Cryptodd.BinanceFutures.Http.RateLimiter;
using Cryptodd.Http;
using Cryptodd.Tests.TestingHelpers.Logging;
using Microsoft.Extensions.Configuration;
using Moq;
using Serilog.Core;
using xRetry;
using Xunit;
using Skip = xRetry.Skip;

namespace Cryptodd.Tests.BinanceFutures.Http;

public class BinancePublicHttpApiRealTest
{
    [RetryFact]
    public async void TestGetExchangeInfoAsync()
    {
        using var httpclient = new HttpClient();
        var config = new ConfigurationBuilder().AddInMemoryCollection(Array.Empty<KeyValuePair<string, string?>>())
            .Build();
        JsonObject res;
        try
        {
            res = await new BinanceFuturesPublicHttpApi(httpclient,
                new Mock<RealLogger>(MockBehavior.Loose) { CallBase = true }.Object, config, new UriRewriteService(),
                new EmptyBinanceFuturesRateLimiter()).GetExchangeInfoAsync();
        }
        catch (HttpRequestException e) when (e.StatusCode is (HttpStatusCode)418 or (HttpStatusCode)429
                                                 or (HttpStatusCode)403)
        {
            Skip.Always(e.ToStringDemystified());
            throw;
        }

        Assert.NotEmpty(res);
        Assert.NotEmpty(res["symbols"] as JsonArray ?? new JsonArray());
    }

    [RetryFact]
    public async void TestGetOrderbook()
    {
        using var httpclient = new HttpClient();
        var config = new ConfigurationBuilder().AddInMemoryCollection(Array.Empty<KeyValuePair<string, string?>>())
            .Build();
        BinanceHttpOrderbook res;
        try
        {
            res = await new BinanceFuturesPublicHttpApi(httpclient,
                new Mock<RealLogger>(MockBehavior.Loose) { CallBase = true }.Object, config, new UriRewriteService(),
                new EmptyBinanceFuturesRateLimiter()).GetOrderbook("ETHUSDT");
        }
        catch (HttpRequestException e) when (e.StatusCode is (HttpStatusCode)418 or (HttpStatusCode)429
                                                 or (HttpStatusCode)403)
        {
            Skip.Always(e.ToStringDemystified());
            throw;
        }

        Assert.NotEmpty(res.Asks);
        Assert.NotEmpty(res.Bids);
    }
}