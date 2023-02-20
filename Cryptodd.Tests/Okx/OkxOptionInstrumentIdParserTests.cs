using System;
using Cryptodd.Okx.Options;
using Xunit;

namespace Cryptodd.Tests.Okx;

public class OkxOptionInstrumentIdParserTests
{
    [Fact]
    public void TestOkxOptionInstrumentIdParser()
    {
        {
            Assert.True(OkxOptionInstrumentIdParser.TryParse("ETH-USD-230119-1300-C", out var eth));

            Assert.Equal("ETH-USD", eth.Underlying);
            Assert.Equal(new DateOnly(2023, 01, 19), eth.Date);
            Assert.Equal(1300, eth.Price);
            Assert.Equal(OkxOptionSide.Call, eth.Side);
        }


        {
            Assert.True(OkxOptionInstrumentIdParser.TryParse("BTC-USD-230331-5000-P", out var btc));

            Assert.Equal("BTC-USD", btc.Underlying);
            Assert.Equal(new DateOnly(2023, 03, 31), btc.Date);
            Assert.Equal(5000, btc.Price);
            Assert.Equal(OkxOptionSide.Put, btc.Side);
        }
    }
}