using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Cryptodd.Binance;
using Cryptodd.Binance.Http;
using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.Ftx;
using Cryptodd.Http;
using Cryptodd.Pairs;
using Microsoft.Extensions.Configuration;
using xRetry;
using Xunit;

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
        using var httpclient = new HttpClient();
        var symbols = await new BinancePublicHttpApi(httpclient, new ConfigurationManager(), new UriRewriteService(), new EmptyBinanceRateLimiter()).ListSymbols();
        Assert.NotEmpty(symbols);
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