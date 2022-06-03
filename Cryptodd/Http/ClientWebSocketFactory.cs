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

    public async ValueTask<ClientWebSocket> GetWebSocket(Uri uri, CancellationToken cancellationToken = default,
        bool connect = true)
    {
        var httpConfig = _configuration.GetSection("Http");
        var ws = new ClientWebSocket { Options = { Proxy = new WebProxy(httpConfig.GetValue<Uri>("Proxy")) } };
        try
        {
            if (connect)
            {
                using var connectToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectToken.CancelAfter(httpConfig.GetValue("ConnectTimeoutMs", 5_000));
                await ws.ConnectAsync(await _uriRewriteService.Rewrite(uri), connectToken.Token).ConfigureAwait(false);
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