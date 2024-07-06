using Cryptodd.Binance.Http;
using Cryptodd.Http;
using Serilog;

namespace Cryptodd.BinanceFutures.Http;

public sealed class BinanceFuturesHttpClientAbstraction(
    HttpClient client,
    ILogger logger,
    IUriRewriteService uriRewriteService)
    : BaseBinanceHttpClientAbstraction(client, logger, uriRewriteService), IBinanceFuturesHttpClientAbstraction;