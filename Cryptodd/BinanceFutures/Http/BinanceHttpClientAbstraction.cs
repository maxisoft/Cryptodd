using Cryptodd.Binance.Http;
using Cryptodd.Http;
using Serilog;

namespace Cryptodd.BinanceFutures.Http;

public sealed class BinanceFuturesHttpClientAbstraction : BaseBinanceHttpClientAbstraction, IBinanceFuturesHttpClientAbstraction
{
    public BinanceFuturesHttpClientAbstraction(HttpClient client, ILogger logger, IUriRewriteService uriRewriteService) : base(
        client, logger, uriRewriteService)
    {
        
    }
}