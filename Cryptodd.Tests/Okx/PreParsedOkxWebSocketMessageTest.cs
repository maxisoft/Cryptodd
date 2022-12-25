using Cryptodd.Okx.Models;
using Xunit;

namespace Cryptodd.Tests.Okx;

public class PreParsedOkxWebSocketMessageTest
{
    [Fact]
    public void TestPreParsedOkxWebSocketMessage()
    {
        var res = PreParsedOkxWebSocketMessage.TryParse(
            "{\"event\":\"subscribe\",\"arg\":{\"channel\":\"books\",\"instId\":\"BTC-USD-SWAP\"}}"u8, out var message);
        Assert.True(res);
        Assert.Equal("subscribe", message.Event);
        Assert.Equal("books", message.ArgChannel);
        Assert.Equal("BTC-USD-SWAP", message.ArgInstrumentId);
    }
}