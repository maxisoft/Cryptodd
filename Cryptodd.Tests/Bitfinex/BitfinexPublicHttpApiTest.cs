using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cryptodd.Bitfinex;
using Cryptodd.Bitfinex.Http;
using Cryptodd.Bitfinex.Http.Abstractions;
using Cryptodd.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using xRetry;
using Xunit;

namespace Cryptodd.Tests.Bitfinex;

public class BitfinexPublicHttpApiTest
{
    public class DirectUri: IUriRewriteService
    {
        public virtual ValueTask<Uri> Rewrite(Uri uri)
        {
            return ValueTask.FromResult<Uri>(uri);
        }
    }
    
    [RetryFact]
    public async Task TestGetAllPairs()
    {
        using var httpclient = new HttpClient();
        
        var uriServiceMock = new Mock<DirectUri>(){CallBase = true};
        var httpClientAbstractionMock = new Mock<BitfinexHttpClientAbstraction>(httpclient, uriServiceMock.Object) { CallBase = true };

        var config = new ConfigurationBuilder().Build();
        var api = new BitfinexPublicHttpApi(httpClientAbstractionMock.Object, config);
        var result = await api.GetAllPairs(CancellationToken.None);
        Assert.NotEmpty(result);
        Assert.Contains("BTCUSD", result);
    }
    
    [RetryFact]
    public async Task TestGetDerivativeStatus()
    {
        using var httpclient = new HttpClient();
        
        var uriServiceMock = new Mock<DirectUri>(){CallBase = true};
        var httpClientAbstractionMock = new Mock<BitfinexHttpClientAbstraction>(httpclient, uriServiceMock.Object) { CallBase = true };

        var config = new ConfigurationBuilder().Build();
        var api = new BitfinexPublicHttpApi(httpClientAbstractionMock.Object, config);
        var result = await api.GetDerivativeStatus(CancellationToken.None);
        Assert.NotEmpty(result);
        Assert.Empty(result.Where(status => string.IsNullOrWhiteSpace(status.Key)));
    }
}