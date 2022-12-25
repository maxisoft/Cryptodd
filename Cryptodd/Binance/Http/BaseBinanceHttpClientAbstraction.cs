using Cryptodd.Http;
using Cryptodd.Http.Abstractions;
using Serilog;

namespace Cryptodd.Binance.Http;

public class BaseBinanceHttpClientAbstraction : HttpClientAbstraction
{
    private readonly ILogger _logger;
    private readonly IUriRewriteService _uriRewriteService;

    protected BaseBinanceHttpClientAbstraction(HttpClient client, ILogger logger,
        IUriRewriteService uriRewriteService) : base(client)
    {
        _logger = logger;
        _uriRewriteService = uriRewriteService;
    }


    public HttpRequestMessage CreateRequestMessage(HttpMethod method, Uri? uri)
    {
        uri = uri is not null ? _uriRewriteService.Rewrite(uri).AsTask().Result : uri;
        return IHttpClientAbstraction.DoCreateRequestMessage(this, method, uri);
    }
}