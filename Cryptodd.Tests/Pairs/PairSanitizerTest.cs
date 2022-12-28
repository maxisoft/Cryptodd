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

public class PairSanitizerTest
{

    [Theory]
    [InlineData("", "")]
    [InlineData("BTC", "BTC")]
    [InlineData("BTC-PERP", "BTC-PERP")]
    [InlineData("ETH-PERP", "ETH-PERP")]
    [InlineData("ETH/USDT", "ETHXUSDT1716f4786210daf6")]
    [InlineData("^DOW", "XDOW14f5a6fa701a0368")]
    public void TestSanitize(string input, string expected)
    {
        Assert.Equal(expected, PairSanitizer.Sanitize(input));
    }
}