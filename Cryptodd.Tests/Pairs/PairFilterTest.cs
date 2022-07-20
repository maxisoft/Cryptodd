using System;
using System.Linq;
using System.Net.Http;
using Cryptodd.Ftx;
using Cryptodd.Http;
using Cryptodd.Pairs;
using Xunit;

namespace Cryptodd.Tests.Pairs;

public class PairFilterTest
{
    [Fact]
    public void TestPairFilter_OneItem()
    {
        {
            var pf = new PairFilter();
            pf.AddAll("BTC-PERP");

            Assert.True(pf.Match("BTC-PERP"));
            Assert.True(pf.Match("btc-perp"));
            Assert.False(pf.Match("eth-perp"));
            Assert.False(pf.Match("eth-usd"));
            Assert.False(pf.Match("eth/usd"));
        }
    }

    [Fact]
    public void TestPairFilter_Regex()
    {
        {
            var pf = new PairFilter();
            pf.AddAll("BTC.*$");

            Assert.True(pf.Match("BTC-PERP"));
            Assert.True(pf.Match("btc-perp"));
            Assert.True(pf.Match("btc/usd"));
            Assert.True(pf.Match("btc/eth"));
            Assert.True(pf.Match("btc13ZDSQDq54"));
            Assert.False(pf.Match("eth-perp"));
            Assert.False(pf.Match("eth-usd"));
            Assert.False(pf.Match("eth/usd"));
        }
    }


    [Fact]
    public void TestPairFilter_USDT_Must_Not_Match_CUSDT()
    {
        var pf = new PairFilter();
        pf.AddAll("USDT-PERP;ETH-PERP");
        Assert.True(pf.Match("USDT-PERP"));
        Assert.False(pf.Match("CUSDT-PERP"));
        
        Assert.Empty(pf.RegexEntries);
    }

    [Fact]
    public void TestPairFilter_items()
    {
        {
            var pf = new PairFilter();
            pf.AddAll("BTC-PERP;ETH-PERP");

            Assert.True(pf.Match("BTC-PERP"));
            Assert.True(pf.Match("btc-perp"));
            Assert.False(pf.Match("btc/usd"));
            Assert.False(pf.Match("btc/eth"));
            Assert.True(pf.Match("eth-perp"));
            Assert.False(pf.Match("eth-usd"));
            Assert.False(pf.Match("eth/usd"));
        }


        {
            var pf = new PairFilter();
            pf.AddAll(" BTC-PERP; ETH-PERP ");

            Assert.True(pf.Match("BTC-PERP"));
            Assert.True(pf.Match("btc-perp"));
            Assert.False(pf.Match("btc/usd"));
            Assert.False(pf.Match("btc/eth"));
            Assert.True(pf.Match("eth-perp"));
            Assert.False(pf.Match("eth-usd"));
            Assert.False(pf.Match("eth/usd"));
        }

        {
            var pf = new PairFilter();
            pf.AddAll(@" BTC-PERP
 ETH-PERP 
BTC-PERP

BTC-PERP
BTC-PERP
BTC-PERP");

            Assert.True(pf.Match("BTC-PERP"));
            Assert.True(pf.Match("btc-perp"));
            Assert.False(pf.Match("btc/usd"));
            Assert.False(pf.Match("btc/eth"));
            Assert.True(pf.Match("eth-perp"));
            Assert.False(pf.Match("eth-usd"));
            Assert.False(pf.Match("eth/usd"));
        }
    }

    [Fact]
    public void TestPairFilter_Empty()
    {
        {
            var pf = new PairFilter();

            // must match all
            Assert.True(pf.Match("BTC-PERP"));
            Assert.True(pf.Match("btc-perp"));
            Assert.True(pf.Match("eth-perp"));
            Assert.True(pf.Match("eth-usd"));
            Assert.True(pf.Match("eth/usd"));
        }

        {
            var pf = new PairFilter();
            pf.AddAll("");

            // must match all
            Assert.True(pf.Match("BTC-PERP"));
            Assert.True(pf.Match("btc-perp"));
            Assert.True(pf.Match("eth-perp"));
            Assert.True(pf.Match("eth-usd"));
            Assert.True(pf.Match("eth/usd"));
        }

        {
            var pf = new PairFilter();
            pf.AddAll("#BTC-PERP"); // commented

            // must match all
            Assert.True(pf.Match("BTC-PERP"));
            Assert.True(pf.Match("btc-perp"));
            Assert.True(pf.Match("eth-perp"));
            Assert.True(pf.Match("eth-usd"));
            Assert.True(pf.Match("eth/usd"));
        }
    }

    [Fact]
    public void TestPairFilter_multiline()
    {
        var data = @"
    BTC:USDT
BTCUSD
BTC:USDT
ETHUSD.{0,2}
_LUNA
#COMMENT
# COMMENT
##COMMENT2
//COMMENTPAR
";
        var pf = new PairFilter();
        pf.AddAll(data);

        Assert.True(pf.Match("BTCUSD"));
        Assert.True(pf.Match("btcUsD"));
        Assert.True(pf.Match("BTC:USDT"));
        Assert.True(pf.Match("_LUNA"));
        Assert.False(pf.Match("LUNA"));
        Assert.True(pf.Match("ethusdt"));
        Assert.True(pf.Match("ethusd"));
        Assert.True(pf.Match("ethusdc"));
        Assert.False(pf.Match("ethdai"));
        
        Assert.False(pf.Match("COMMENT"));
        Assert.False(pf.Match("#COMMENT"));
        Assert.False(pf.Match("//COMMENTPAR"));
        Assert.False(pf.Match("COMMENTPAR"));
    }
}