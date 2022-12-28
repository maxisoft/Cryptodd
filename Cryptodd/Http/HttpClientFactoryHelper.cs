using System.Net;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Extensions.Http;

namespace Cryptodd.Http;

public interface IHttpClientFactoryHelper
{
    void Configure(HttpClient client);
    IAsyncPolicy<HttpResponseMessage> GetRetryPolicy();
    SocketsHttpHandler GetHandler();
}

// ReSharper disable once UnusedType.Global
public class HttpClientFactoryHelper : IHttpClientFactoryHelper
{
    private readonly IConfigurationSection _httpConfig;

    public HttpClientFactoryHelper(IConfiguration configuration)
    {
        _httpConfig = configuration.GetSection("Http");
    }

    public void Configure(HttpClient client)
    {
        client.Timeout = TimeSpan.FromMilliseconds(_httpConfig.GetValue("TimeoutMs", 5 * 1000));
        client.MaxResponseContentBufferSize = _httpConfig.GetValue<long>("MaxResponseContentBufferSize", 64 << 20);
        var userAgent =
            _httpConfig.GetValue<string>("UserAgent", $"Crytodd v{GetType().Assembly.GetName().Version}");
        if (!string.IsNullOrEmpty(userAgent))
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }
    }

    public SocketsHttpHandler GetHandler()
    {
        var handler = new SocketsHttpHandler
        {
            MaxConnectionsPerServer = _httpConfig.GetValue("MaxConnectionsPerServer", 32),
            PooledConnectionLifetime = _httpConfig.GetValue("PooledConnectionLifetime", TimeSpan.FromMinutes(5)),
            PooledConnectionIdleTimeout = _httpConfig.GetValue("PooledConnectionIdleTimeout", TimeSpan.FromMinutes(1)),
            KeepAlivePingDelay = _httpConfig.GetValue("KeepAlivePingDelay", TimeSpan.FromSeconds(10)),
            ConnectTimeout = _httpConfig.GetValue("ConnectTimeout", TimeSpan.FromSeconds(5))
        };

        var proxyString = _httpConfig.GetValue("Proxy", string.Empty);
        if (Uri.TryCreate(proxyString, UriKind.Absolute, out var proxyUri))
        {
            var proxy = new WebProxy
            {
                Address = proxyUri
            };
            handler.Proxy = proxy;
        }

        return handler;
    }

    public IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => msg.StatusCode == HttpStatusCode.NotFound)
            .WaitAndRetryAsync(_httpConfig.GetValue("NumRetry", 6), retryAttempt =>
                TimeSpan.FromMilliseconds(Math.Pow(2,
                    retryAttempt)) * _httpConfig.GetValue("RetryMs", 100));
    }
}