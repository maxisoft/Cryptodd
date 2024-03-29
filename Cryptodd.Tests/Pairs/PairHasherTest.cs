﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Cryptodd.Binance;
using Cryptodd.Binance.Http;
using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.Bitfinex;
using Cryptodd.Bitfinex.Http;
using Cryptodd.Bitfinex.Http.Abstractions;
using Cryptodd.Ftx;
using Cryptodd.Http;
using Cryptodd.Pairs;
using Cryptodd.Tests.Bitfinex;
using Cryptodd.Tests.TestingHelpers;
using Cryptodd.Tests.TestingHelpers.Logging;
using Microsoft.Extensions.Configuration;
using Moq;
using Serilog.Core;
using xRetry;
using Xunit;
using Skip = xRetry.Skip;

namespace Cryptodd.Tests.Pairs;

public class PairHasherTest
{
    [Fact]
    public void TestHashPreconditions()
    {
#pragma warning disable xUnit2000
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes("test")).Length, PairHasher.Sha256ByteCount);
#pragma warning restore xUnit2000
        Assert.True(PairHasher.Sha256ByteCount / sizeof(long) > 0);
        Assert.True(PairHasher.Sha256ByteCount % sizeof(long) == 0);
        Assert.True(BitConverter.IsLittleEndian);
    }
    
    [Fact]
    public void TestHash()
    {
        Assert.Equal(0, PairHasher.Hash(""));
        Assert.Equal(7966697908167619883, PairHasher.Hash("BTC-PERP"));
        Assert.Equal(3846482641729352903, PairHasher.Hash("ETH-PERP"));

        Parallel.For(0, 128, _ =>
        {
            Assert.Equal(0, PairHasher.Hash(""));
            Assert.Equal(7966697908167619883, PairHasher.Hash("BTC-PERP"));
            Assert.Equal(3846482641729352903, PairHasher.Hash("ETH-PERP"));
        });
        
        Assert.True(PairHasher.Hash("LONGLONGLONGLONGLONGLONGLONGLONGLONGLONGLONGLONGLONGLONGLONGLONGLONGLONGLONGLONGLONGLONGLONGLONGLONGLONGLONG") > 0);
    }
    
    [RetryFact]
    public async Task TestRealHash()
    {
        var cancellationToken = CancellationToken.None;
        using var httpclient = new HttpClient();
        
        var uriServiceMock = new Mock<BitfinexPublicHttpApiTest.DirectUri>(){CallBase = true};
        var httpClientAbstractionMock = new Mock<BitfinexHttpClientAbstraction>(httpclient, uriServiceMock.Object) { CallBase = true };

        var config = new ConfigurationBuilder().Build();
        List<string> symbols;
        var client = new BinanceHttpClientAbstraction(httpclient, new Mock<RealLogger>() { CallBase = true }.Object,
            new Mock<MockableUriRewriteService>() { CallBase = true }.Object);
        try
        {
            symbols = await new BinancePublicHttpApi(client, new Mock<RealLogger>(MockBehavior.Loose){CallBase = true}.Object, new ConfigurationManager(), new EmptyBinanceRateLimiter()).ListSymbols(cancellationToken:cancellationToken);
        }
        catch (HttpRequestException e) when (e.StatusCode is (HttpStatusCode)418 or (HttpStatusCode)429 or (HttpStatusCode) 451 
                                                 or (HttpStatusCode)403)
        {
            Debug.Write(e.ToStringDemystified());
            symbols = new List<string>();
        }

        try
        {
            var tmp = await new BitfinexPublicHttpApi(httpClientAbstractionMock.Object, config)
                .GetAllPairs(cancellationToken);
            symbols.AddRange(tmp);
        }
        catch (HttpRequestException e)
        {
            Debug.Write(e.ToStringDemystified());
        }
        
        //Assert.NotEmpty(symbols);
        var marketsUnique = symbols.Where(s => !string.IsNullOrEmpty(s)).ToImmutableHashSet();

        var hashes = new ConcurrentBag<long>();
        Parallel.ForEach(marketsUnique, s => hashes.Add(PairHasher.Hash(s)));

        Assert.Equal(marketsUnique.Count, hashes.ToImmutableHashSet().Count);
        foreach (var hash in hashes)
        {
            Assert.True(hash > 0);
        }
    }
}