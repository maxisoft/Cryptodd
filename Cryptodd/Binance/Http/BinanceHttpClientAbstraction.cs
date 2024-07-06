using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.Http;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Binance.Http;

public sealed class BinanceHttpClientAbstraction(
    HttpClient client,
    ILogger logger,
    IUriRewriteService uriRewriteService)
    : BaseBinanceHttpClientAbstraction(client, logger, uriRewriteService), IBinanceHttpClientAbstraction;