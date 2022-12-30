using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Cryptodd.Okx.Http;
using Cryptodd.Okx.Http.Abstractions;
using Cryptodd.Okx.Limiters;
using Cryptodd.Okx.Models;
using Cryptodd.Tests.TestingHelpers;
using Cryptodd.Tests.TestingHelpers.Logging;
using Microsoft.Extensions.Configuration;
using Moq;
using xRetry;
using Xunit;
using Skip = xRetry.Skip;

namespace Cryptodd.Tests.Okx;

public class OkxPublicHttpApiTests
{
    [RetryFact]
    public async Task RealTestListInstrumentIds()
    {
        using var httpclient = new HttpClient();
        var logger = new Mock<RealLogger>() { CallBase = true }.Object;
        var uriRewriteService = new Mock<MockableUriRewriteService>() { CallBase = true }.Object;
        var config = new ConfigurationBuilder().AddInMemoryCollection(Array.Empty<KeyValuePair<string, string?>>())
            .Build();
        var clientAbstraction = new OkxHttpClientAbstraction(httpclient, logger, uriRewriteService, new OkxLimiterRegistry(config));
        var api = new OkxPublicHttpApi(clientAbstraction, config);

        List<string> res;
        try
        {
            res = await api.ListInstrumentIds(OkxInstrumentType.Spot);
        }
        catch (HttpRequestException e) when (e.StatusCode is (HttpStatusCode)418 or (HttpStatusCode)429
                                                 or (HttpStatusCode)451
                                                 or (HttpStatusCode)403)
        {
            Skip.Always(e.ToStringDemystified());
            throw;
        }
        Assert.NotEmpty(res);
        Assert.Contains("BTC-USDT", res);
        
        res = await api.ListInstrumentIds(OkxInstrumentType.Margin);
        Assert.NotEmpty(res);
        Assert.Contains("BTC-USDT", res);
        
        
        res = await api.ListInstrumentIds(OkxInstrumentType.Swap);
        Assert.NotEmpty(res);
        Assert.Contains("BTC-USDT-SWAP", res);
        
        res = await api.ListInstrumentIds(OkxInstrumentType.Futures);
        Assert.NotEmpty(res);


        res = await api.ListInstrumentIds(OkxInstrumentType.Option, instrumentFamily: "BTC-USD");
        Assert.NotEmpty(res);
    }
    
    
    [RetryFact]
    public async Task RealTestGetTickers()
    {
        using var httpclient = new HttpClient();
        var logger = new Mock<RealLogger>() { CallBase = true }.Object;
        var uriRewriteService = new Mock<MockableUriRewriteService>() { CallBase = true }.Object;
        var config = new ConfigurationBuilder().AddInMemoryCollection(Array.Empty<KeyValuePair<string, string?>>())
            .Build();
        var clientAbstraction = new OkxHttpClientAbstraction(httpclient, logger, uriRewriteService, new OkxLimiterRegistry(config));
        var api = new OkxPublicHttpApi(clientAbstraction, config);

        OkxHttpGetTikersResponse res;
        try
        {
            res = await api.GetTickers(OkxInstrumentType.Spot);
        }
        catch (HttpRequestException e) when (e.StatusCode is (HttpStatusCode)418 or (HttpStatusCode)429
                                                 or (HttpStatusCode)451
                                                 or (HttpStatusCode)403)
        {
            Skip.Always(e.ToStringDemystified());
            throw;
        }
        Assert.NotEmpty(res.data);

        res = await api.GetTickers(OkxInstrumentType.Swap);
        Assert.NotEmpty(res.data);

        res = await api.GetTickers(OkxInstrumentType.Futures);
        Assert.NotEmpty(res.data);


        res = await api.GetTickers(OkxInstrumentType.Option, instrumentFamily: "BTC-USD");
        Assert.NotEmpty(res.data);
    }
    
    [RetryFact]
    public async Task RealTestGetOpenInterest()
    {
        using var httpclient = new HttpClient();
        var logger = new Mock<RealLogger>() { CallBase = true }.Object;
        var uriRewriteService = new Mock<MockableUriRewriteService>() { CallBase = true }.Object;
        var config = new ConfigurationBuilder().AddInMemoryCollection(Array.Empty<KeyValuePair<string, string?>>())
            .Build();
        var clientAbstraction = new OkxHttpClientAbstraction(httpclient, logger, uriRewriteService, new OkxLimiterRegistry(config));
        var api = new OkxPublicHttpApi(clientAbstraction, config);

        OkxHttpGetOpenInterestResponse res;
        try
        {
            res = await api.GetOpenInterest(OkxInstrumentType.Swap);
        }
        catch (HttpRequestException e) when (e.StatusCode is (HttpStatusCode)418 or (HttpStatusCode)429
                                                 or (HttpStatusCode)451
                                                 or (HttpStatusCode)403)
        {
            Skip.Always(e.ToStringDemystified());
            throw;
        }
        Assert.NotEmpty(res.data);

        res = await api.GetOpenInterest(OkxInstrumentType.Futures);
        Assert.NotEmpty(res.data);


        res = await api.GetOpenInterest(OkxInstrumentType.Option, instrumentFamily: "BTC-USD");
        Assert.NotEmpty(res.data);
    }
    
    
    [RetryFact]
    public async Task RealTestGetFundingRate()
    {
        using var httpclient = new HttpClient();
        var logger = new Mock<RealLogger>() { CallBase = true }.Object;
        var uriRewriteService = new Mock<MockableUriRewriteService>() { CallBase = true }.Object;
        var config = new ConfigurationBuilder().AddInMemoryCollection(Array.Empty<KeyValuePair<string, string?>>())
            .Build();
        var clientAbstraction = new OkxHttpClientAbstraction(httpclient, logger, uriRewriteService, new OkxLimiterRegistry(config));
        var api = new OkxPublicHttpApi(clientAbstraction, config);

        OkxHttpGetFundingRateResponse res;
        try
        {
            res = await api.GetFundingRate("BTC-USD-SWAP");
        }
        catch (HttpRequestException e) when (e.StatusCode is (HttpStatusCode)418 or (HttpStatusCode)429
                                                 or (HttpStatusCode)451
                                                 or (HttpStatusCode)403)
        {
            Skip.Always(e.ToStringDemystified());
            throw;
        }
        Assert.NotEmpty(res.data);
    }
}