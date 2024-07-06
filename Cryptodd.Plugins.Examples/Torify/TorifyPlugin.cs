/*
 * This file is a plugin for cryptodd.
 * It allow one to use tor for some/most websocket connections to exchanges.
 * It add a Torify configuration section to configure it further
 * Place it under plugins/Torify folder to activate it.
 * Note that it has to be in a subfolder to be loaded !
 */


#nullable enable

using System;
// ReSharper disable RedundantUsingDirective
using System.Globalization;
using System.IO;
// ReSharper restore RedundantUsingDirective
using System.Net;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Cryptodd.Binance.Orderbooks.Websockets;
using Cryptodd.BinanceFutures.Orderbooks.Websockets;
using Cryptodd.Bitfinex.WebSockets;
using Cryptodd.Http;
using Cryptodd.Okx.Websockets;
using Lamar;
using Maxisoft.Utils.Disposables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;


namespace Cryptodd.Plugins.Examples.Torify;

public class TorifyOptions
{
    public bool Enabled { get; set; } = true;
    public string ProxyAddress { get; set; } = "socks5://127.0.0.1:9050";

    public bool UseForBinance { get; set; } = true;
    public bool UseForBinanceFutures { get; set; } = true;
    public bool UseForBitfinex { get; set; } = true;
    
    public bool UseForOkx { get; set; } = true;

    public bool DebugLog { get; set; }
    
    public int? ConnectTimeoutMs { get; set; }
}

public class TorifyPlugin : BasePlugin
{
    private readonly IClientWebSocketFactory? _previousClientWebSocketFactory;

    // Create a public constructor with a Lamar.IContainer argument
    // ioc (inversion of control) may help one to inject any other service into the plugin
    public TorifyPlugin(IContainer container) : base(container)
    {
        _previousClientWebSocketFactory = container.GetService<IClientWebSocketFactory>();
        // reconfigure ioc to use our IClientWebSocketFactory
        container.Configure(collection =>
        {
            collection.AddSingleton<IClientWebSocketFactory>(provider => new TorifyClientWebSocketFactory(
                provider.GetRequiredService<IConfiguration>(),
                _previousClientWebSocketFactory, provider));
        });
    }
}

public sealed class TorifyClientWebSocketFactory : IClientWebSocketFactory, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly DisposableManager _disposableManager = new();
    private readonly IUriRewriteService _uriRewriteService;
    private readonly ILogger _logger;

    public TorifyClientWebSocketFactory(IConfiguration configuration,
        IClientWebSocketFactory? previousClientWebSocketFactory, IServiceProvider container)
    {
        _logger = container.GetRequiredService<ILogger>().ForContext(GetType());
        _configuration = configuration;
        _previousClientWebSocketFactory =
            previousClientWebSocketFactory ?? container.GetRequiredService<IClientWebSocketFactory>();
        if (_previousClientWebSocketFactory is TorifyClientWebSocketFactory)
        {
            _previousClientWebSocketFactory = container.GetRequiredService<ClientWebSocketFactory>();
        }

        _uriRewriteService = container.GetRequiredService<IUriRewriteService>();
        var section = configuration.GetSection("Torify");
        BindConfiguration(section);
        var cb = section.GetReloadToken().RegisterChangeCallback(_ => BindConfiguration(section), this);
        _disposableManager.LinkDisposable(cb);
    }

    private readonly TorifyOptions _options = new();
    private readonly IClientWebSocketFactory _previousClientWebSocketFactory;

    private void BindConfiguration(IConfiguration configuration)
    {
        configuration.Bind(_options);
    }

    public async ValueTask<ClientWebSocket> GetWebSocket(Uri uri, bool connect = true,
        CancellationToken cancellationToken = default)
    {
        ValueTask<ClientWebSocket> BaseCall()
        {
            return _previousClientWebSocketFactory.GetWebSocket(uri, connect, cancellationToken);
        }

        if (!_options.Enabled)
        {
            return await BaseCall();
        }

        uri = await _uriRewriteService.Rewrite(uri);
        if (!ShouldIntercept(uri))
        {
            return await BaseCall();
        }

        try
        {
            return await DoGetWebSocket(uri, connect, cancellationToken);
        }
        catch (Exception e)
        {
            if (_options.DebugLog)
            {
                _logger.Debug(e, "Unable to torify ws to {Uri}", new UriToStringHelper(uri));
            }

            return await BaseCall();
        }
    }

    private readonly Lazy<string> _binanceHost =
        new(() => new Uri(BinanceOrderbookWebsocketOptions.DefaultBaseAddress).Host);

    private readonly Lazy<string> _binanceFuturesHost =
        new(() => new Uri(BinanceFuturesOrderbookWebsocketOptions.DefaultBaseAddress).Host);

    private readonly Lazy<string> _bitfinexHost = new(() => new Uri(BitfinexPublicWebSocketOptions.DefaultUrl).Host);
    
    private readonly Lazy<string> _okxHost = new(() => new Uri(BaseOkxWebsocketOptions.DefaultAddress).Host);

    private bool ShouldIntercept(Uri uri)
    {
        var host = uri.Host;

        if (_options.UseForBinanceFutures && host == _binanceFuturesHost.Value)
        {
            return true;
        }

        if (_options.UseForBinance && host == _binanceHost.Value)
        {
            return true;
        }

        if (_options.UseForBitfinex && host == _bitfinexHost.Value)
        {
            return true;
        }

        if (_options.UseForOkx && host == _okxHost.Value)
        {
            return true;
        }

        return false;
    }

    private async ValueTask<ClientWebSocket> DoGetWebSocket(Uri uri, bool connect = true,
        CancellationToken cancellationToken = default)
    {
        var httpConfig = _configuration.GetSection("Http");
        var ws = new ClientWebSocket
        {
            Options =
            {
                Proxy = new WebProxy(!string.IsNullOrWhiteSpace(_options.ProxyAddress)
                    ? new Uri(_options.ProxyAddress)
                    : httpConfig.GetValue<Uri>("Proxy"))
            }
        };
        try
        {
            {
                using var connectToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectToken.CancelAfter(_options.ConnectTimeoutMs ?? httpConfig.GetValue("ConnectTimeoutMs", 2_000));
                await ws.ConnectAsync(uri, connectToken.Token).ConfigureAwait(false);
                if (!connect)
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, connectToken.Token)
                        .ConfigureAwait(false);
                }
            }
            if (_options.DebugLog)
            {
                _logger.Debug("Successfully opened a ws to {Uri} via tor", new UriToStringHelper(uri));
            }

            return ws;
        }
        catch (Exception)
        {
            ws.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _disposableManager.Dispose();
        GC.SuppressFinalize(this);
    }

    private readonly struct UriToStringHelper
    {
        private readonly Uri _uri;

        public UriToStringHelper(Uri uri)
        {
            _uri = uri;
        }

        public override string ToString()
        {
            if (_uri.PathAndQuery.Length > 20)
            {
                return _uri.Host;
            }

            var ts = _uri.ToString();
            return ts.Length > 20 || ts.Contains("secret") || ts.Contains("key") || ts.Contains("private")
                ? _uri.Host
                : ts;
        }
    }
}