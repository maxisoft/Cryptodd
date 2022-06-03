using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Cryptodd.Ftx;
using Cryptodd.Http;
using Cryptodd.Pairs;
using xRetry;
using Xunit;

namespace Cryptodd.Tests.Pairs;

public class PairHasherTest
{
    [Fact]
    public void TestHashPreconditions()
    {
        Assert.Equal(SHA256.HashData(Encoding.UTF8.GetBytes("test")).Length, PairHasher.Sha256ByteCount);
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
        var markets = await new FtxPublicHttpApi(httpclient, new UriRewriteService()).GetAllMarketsAsync();
        Assert.NotEmpty(markets);
        var marketsUnique = markets.Select(market => market.Name).Where(s => !string.IsNullOrEmpty(s)).ToImmutableHashSet();

        var hashes = new ConcurrentBag<long>();
        Parallel.ForEach(marketsUnique, s => hashes.Add(PairHasher.Hash(s)));

        Assert.Equal(marketsUnique.Count, hashes.ToImmutableHashSet().Count);
        foreach (var hash in hashes)
        {
            Assert.True(hash > 0);
        }
    }
}