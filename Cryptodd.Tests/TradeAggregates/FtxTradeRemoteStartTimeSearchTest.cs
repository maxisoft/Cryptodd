using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Cryptodd.Ftx;
using Cryptodd.Http;
using Cryptodd.TradeAggregates;
using Moq;
using xRetry;
using Xunit;

namespace Cryptodd.Tests.TradeAggregates;

public class FtxTradeRemoteStartTimeSearchTest
{
    [RetryFact]
    public async void TestUsingRealCall()
    {
        using var httpclient = new HttpClient();
        var ftxClient = new FtxPublicHttpApi(httpclient, new UriRewriteService());
        var mock = new Mock<IFtxPublicHttpApi>();
        mock.Setup(api =>
                api.GetTradesAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(),
                    It.IsAny<CancellationToken>()))
            .Returns((string market, long start, long end, CancellationToken token) =>
                ftxClient.GetTradesAsync(market, start, end, token));
        var timeSearch = new FtxTradeRemoteStartTimeSearch(mock.Object, "BTC/USD");
        var result = await timeSearch.Search(default);
        Assert.True(result <= DateTimeOffset.Parse("2020/1/2 00:00:00", CultureInfo.InvariantCulture)); // this algorithm is not designed to be precise
        mock.Verify(api => api.GetTradesAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        mock.Verify(api => api.GetTradesAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(),
            It.IsAny<CancellationToken>()), Times.AtMost(timeSearch.MaxApiCall));

        mock.VerifyNoOtherCalls();
    }
}