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
using Cryptodd.Tests.TestingHelpers;
using Cryptodd.Tests.TestingHelpers.Logging;
using Maxisoft.Utils.Collections.Lists.Specialized;
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
        var client = new BinanceFuturesHttpClientAbstraction(httpclient, new Mock<RealLogger>() { CallBase = true }.Object,
            new Mock<MockableUriRewriteService>() { CallBase = true }.Object);
        JsonObject res;
        try
        {
            res = await new BinanceFuturesPublicHttpApi(client,
                new Mock<RealLogger>(MockBehavior.Loose) { CallBase = true }.Object, config,
                new EmptyBinanceFuturesRateLimiter()).GetExchangeInfoAsync();
        }
        catch (HttpRequestException e) when (e.StatusCode is (HttpStatusCode)418 or (HttpStatusCode)429 or (HttpStatusCode) 451 
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
        var client = new BinanceFuturesHttpClientAbstraction(httpclient, new Mock<RealLogger>() { CallBase = true }.Object,
            new Mock<MockableUriRewriteService>() { CallBase = true }.Object);
        BinanceHttpOrderbook res;
        try
        {
            res = await new BinanceFuturesPublicHttpApi(client,
                new Mock<RealLogger>(MockBehavior.Loose) { CallBase = true }.Object, config,
                new EmptyBinanceFuturesRateLimiter()).GetOrderbook("ETHUSDT");
        }
        catch (HttpRequestException e) when (e.StatusCode is (HttpStatusCode)418 or (HttpStatusCode)429 or (HttpStatusCode) 451 
                                                 or (HttpStatusCode)403)
        {
            Skip.Always(e.ToStringDemystified());
            throw;
        }

        Assert.NotEmpty(res.Asks);
        Assert.NotEmpty(res.Bids);
    }
    
    [RetryFact]
    public async void TestGetKlines()
    {
        using var httpclient = new HttpClient();
        var config = new ConfigurationBuilder().AddInMemoryCollection(Array.Empty<KeyValuePair<string, string?>>())
            .Build();
        var client = new BinanceFuturesHttpClientAbstraction(httpclient, new Mock<RealLogger>() { CallBase = true }.Object,
            new Mock<MockableUriRewriteService>() { CallBase = true }.Object);

        PooledList<BinanceHttpKline> res;
        try
        {
            res = await new BinanceFuturesPublicHttpApi(client,
                new Mock<RealLogger>(MockBehavior.Loose) { CallBase = true }.Object, config,
                new EmptyBinanceFuturesRateLimiter()).GetKlines("BTCUSDT");
        }
        catch (HttpRequestException e) when (e.StatusCode is (HttpStatusCode)418 or (HttpStatusCode)429
                                                 or (HttpStatusCode)451
                                                 or (HttpStatusCode)403)
        {
            Skip.Always(e.ToStringDemystified());
            throw;
        }

        Assert.NotEmpty(res);
    }
    
    [RetryFact]
    public async void TestGetServerTime()
    {
        using var httpclient = new HttpClient();
        var config = new ConfigurationBuilder().AddInMemoryCollection(Array.Empty<KeyValuePair<string, string?>>())
            .Build();
        var client = new BinanceFuturesHttpClientAbstraction(httpclient, new Mock<RealLogger>() { CallBase = true }.Object,
            new Mock<MockableUriRewriteService>() { CallBase = true }.Object);

        BinanceHttpServerTime res;
        try
        {
            res = await new BinanceFuturesPublicHttpApi(client,
                new Mock<RealLogger>(MockBehavior.Loose) { CallBase = true }.Object, config,
                new EmptyBinanceFuturesRateLimiter()).GetServerTime();
        }
        catch (HttpRequestException e) when (e.StatusCode is (HttpStatusCode)418 or (HttpStatusCode)429
                                                 or (HttpStatusCode)451
                                                 or (HttpStatusCode)403)
        {
            Skip.Always(e.ToStringDemystified());
            throw;
        }

        Assert.True(res.serverTime >= 0);
    }
}