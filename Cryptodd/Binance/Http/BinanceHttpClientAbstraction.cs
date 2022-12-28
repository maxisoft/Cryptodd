using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.Http;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Binance.Http;

public sealed class BinanceHttpClientAbstraction : BaseBinanceHttpClientAbstraction, IBinanceHttpClientAbstraction
{
    public BinanceHttpClientAbstraction(HttpClient client, ILogger logger, IUriRewriteService uriRewriteService) : base(
        client, logger, uriRewriteService)
    {
        
    }
}