using System.Text;
using Cryptodd.Binance.Orderbooks.Websockets;
using Cryptodd.Http;
using Cryptodd.IoC;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.BinanceFutures.Orderbooks.Websockets;

public class BinanceFuturesOrderbookWebsocketOptions : BaseBinanceOrderbookWebsocketOptions
{
    public const string DefaultBaseAddress = "wss://fstream.binance.com";

    public BinanceFuturesOrderbookWebsocketOptions()
    {
        if (string.IsNullOrEmpty(BaseAddress))
        {
            BaseAddress = DefaultBaseAddress;
        }
    }
}

public class BinanceFuturesOrderbookWebsocket : BaseBinanceOrderbookWebsocket<BinanceFuturesOrderbookWebsocketOptions>,
    IService
{
    public BinanceFuturesOrderbookWebsocket(ILogger logger, IClientWebSocketFactory webSocketFactory,
        Boxed<CancellationToken> cancellationToken, IConfiguration configuration) : base(logger, webSocketFactory,
        cancellationToken)
    {
        var section = configuration.GetSection("BinanceFutures:Orderbook:Websocket");
        Options = new BinanceFuturesOrderbookWebsocketOptions();
        section.Bind(Options);
    }

    protected override Uri CreateUri()
    {
        if (!_trackedDepthSymbols.Any())
        {
            throw new ArgumentException("doesn't contains any tracked depth symbol", nameof(_trackedDepthSymbols));
        }

        static void TrimEnd(StringBuilder stringBuilder, char c = '/')
        {
            while (stringBuilder.Length > 0 && stringBuilder[^1] == c)
            {
                stringBuilder.Length -= 1;
            }
        }

        var sb = new StringBuilder(Options.BaseAddress);
        TrimEnd(sb, ' ');
        TrimEnd(sb);


        sb.Append("/stream?streams=");
        foreach (var (symbol, _) in _trackedDepthSymbols)
        {
            if (IsBlacklistedSymbol(symbol))
            {
                continue;
            }

            sb.Append(symbol.ToLowerInvariant());
            sb.Append("@depth@500ms");
            sb.Append('/');
        }

        TrimEnd(sb);

        return new Uri(sb.ToString());
    }
}

