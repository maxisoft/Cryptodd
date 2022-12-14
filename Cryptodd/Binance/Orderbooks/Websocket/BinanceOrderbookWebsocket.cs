using Cryptodd.Http;
using Cryptodd.IoC;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Binance.Orderbooks.Websocket;

public class BinanceOrderbookWebsocket : BaseBinanceOrderbookWebsocket<BinanceOrderbookWebsocketOptions>, IService
{
    public BinanceOrderbookWebsocket(ILogger logger, IClientWebSocketFactory webSocketFactory,
        Boxed<CancellationToken> cancellationToken, IConfiguration configuration) : base(logger, webSocketFactory,
        cancellationToken)
    {
        var section = configuration.GetSection("Binance:Orderbook:Websocket");
        Options = new BinanceOrderbookWebsocketOptions();
        section.Bind(Options);
    }
}