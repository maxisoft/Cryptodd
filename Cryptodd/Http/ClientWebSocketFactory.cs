using System.Net;
using System.Net.WebSockets;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.Http;

public class ClientWebSocketFactory : IClientWebSocketFactory
{
    private readonly IConfiguration _configuration;
    private readonly IUriRewriteService _uriRewriteService;

    public ClientWebSocketFactory(IConfiguration configuration, IUriRewriteService uriRewriteService)
    {
        _configuration = configuration;
        _uriRewriteService = uriRewriteService;
    }

    public async ValueTask<ClientWebSocket> GetWebSocket(Uri uri,
        bool connect = true, CancellationToken cancellationToken = default)
    {
        var httpConfig = _configuration.GetSection("Http");
        var proxyAddress = httpConfig.GetValue<Uri?>("Proxy");
        var ws = new ClientWebSocket
            { Options = { Proxy = proxyAddress is not null ? new WebProxy(proxyAddress) : null } };
        try
        {
            if (connect)
            {
                var connectTimeoutMs = TimeSpan.FromMilliseconds(httpConfig.GetValue("ConnectTimeoutMs", 5_000));
                using var connectToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectToken.CancelAfter(connectTimeoutMs);
                var handler = new SocketsHttpHandler()
                {
                    ConnectTimeout = connectTimeoutMs,
                    Proxy = ws.Options.Proxy,
                    UseProxy = proxyAddress is not null
                };
                //handler.ConnectCallback = async (context, token) => { }
                var invoker = new HttpMessageInvoker(handler, disposeHandler: true);
                await ws.ConnectAsync(await _uriRewriteService.Rewrite(uri), invoker, connectToken.Token)
                    .ConfigureAwait(false);
            }

            return ws;
        }
        catch (Exception)
        {
            ws.Dispose();
            throw;
        }
    }
}