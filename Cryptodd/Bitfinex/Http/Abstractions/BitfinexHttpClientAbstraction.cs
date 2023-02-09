using Cryptodd.Http;
using Cryptodd.Http.Abstractions;

namespace Cryptodd.Bitfinex.Http.Abstractions;

public class BitfinexHttpClientAbstraction :
    HttpClientAbstractionWithUriRewrite<IUriRewriteService, HttpClientAbstractionContext>, IBitfinexHttpClientAbstraction
{
    public BitfinexHttpClientAbstraction(HttpClient client,
        IUriRewriteService uriRewriteService) : base(client, uriRewriteService)
    {
    }


    protected override HttpClientAbstractionContext DefaultContext() => new();
}