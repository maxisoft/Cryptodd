using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Cryptodd.IoC;
using Lamar;
using Maxisoft.Utils.Collections.Dictionaries;
using Maxisoft.Utils.Empties;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Extensions.Http;
using TurnerSoftware.DinoDNS;
using TurnerSoftware.DinoDNS.Protocol;

namespace Cryptodd.Http;

public interface IHttpClientFactoryHelper
{
    void Configure(HttpClient client);
    IAsyncPolicy<HttpResponseMessage> GetRetryPolicy();
    SocketsHttpHandler GetHandler();
}

public interface IDnsClientFactory
{
    DnsClient GetDnsClient();
}

[Singleton]
public class DnsClientFactory : IService, IDnsClientFactory
{
    public DnsClient GetDnsClient()
    {
        return new DnsClient([
            NameServers.Cloudflare.IPv4.GetPrimary(ConnectionType.DoH),
            NameServers.Cloudflare.IPv6.GetPrimary(ConnectionType.DoH)
        ], new DnsMessageOptions() { MaximumMessageSize = DnsMessageOptions.DefaultCompatibleMessageSize });
    }
}

// ReSharper disable once UnusedType.Global
public class HttpClientFactoryHelper : IHttpClientFactoryHelper
{
    private readonly IConfigurationSection _httpConfig;
    private readonly Lazy<IDnsClientFactory> _dnsClientFactory;

    public HttpClientFactoryHelper(IConfiguration configuration, Lazy<IDnsClientFactory> dnsClientFactory)
    {
        _httpConfig = configuration.GetSection("Http");
        _dnsClientFactory = dnsClientFactory;
    }

    public void Configure(HttpClient client)
    {
        client.Timeout = TimeSpan.FromMilliseconds(_httpConfig.GetValue("TimeoutMs", 5 * 1000));
        client.MaxResponseContentBufferSize = _httpConfig.GetValue<long>("MaxResponseContentBufferSize", 64 << 20);
        var userAgent =
            _httpConfig.GetValue<string>("UserAgent", $"Cryptodd v{GetType().Assembly.GetName().Version}");
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


        if (_httpConfig.GetValue<bool>("Dns", false))
        {
            handler.ConnectCallback = HandlerConnectCallback;
        }

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

    private async ValueTask<Stream> HandlerConnectCallback(SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var dnsClient = _dnsClientFactory.Value.GetDnsClient();
        // Query DNS and get the list of IPAddress
        var dnsMessageATask = dnsClient
            .QueryAsync(context.DnsEndPoint.Host, DnsQueryType.A, cancellationToken: cancellationToken).AsTask();
        // ReSharper disable once InconsistentNaming
        var dnsMessageAAAATask = dnsClient
            .QueryAsync(context.DnsEndPoint.Host, DnsQueryType.AAAA, cancellationToken: cancellationToken).AsTask();

        var records = new OrderedDictionary<ResourceRecord, EmptyStruct>(new ResourceRecordComparer());

        void UpdateRecords()
        {
            if (dnsMessageATask.IsCompletedSuccessfully)
            {
                foreach (var record in dnsMessageATask.Result.Answers)
                {
                    records.Add(record, default);
                }
            }

            if (dnsMessageAAAATask.IsCompletedSuccessfully)
            {
                foreach (var record in dnsMessageAAAATask.Result.Answers)
                {
                    records.Add(record, default);
                }
            }
        }

        var tasks = new[]
        {
            dnsMessageATask, dnsMessageAAAATask,
            dnsMessageATask.ContinueWith(_ => UpdateRecords(), cancellationToken),
            dnsMessageAAAATask.ContinueWith(_ => UpdateRecords(), cancellationToken)
        };

        await Task.WhenAny(tasks).ConfigureAwait(false);
        UpdateRecords();

        if (records.Count == 0)
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
            UpdateRecords();
            if (records.Count == 0)
            {
                if (dnsMessageATask.IsFaulted)
                {
                    await dnsMessageATask;
                }

                if (dnsMessageAAAATask.IsFaulted)
                {
                    await dnsMessageAAAATask;
                }

                throw new Exception($"Cannot resolve domain '{context.DnsEndPoint.Host}'");
            }
        }


        var addresses = new List<IPAddress>();
        foreach (var (record, _) in records)
        {
            if (record.Type is DnsType.A or DnsType.AAAA)
            {
                addresses.Add(new IPAddress(record.Data.Span));
            }
        }

        // Connect to the remote host
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp)
        {
            // Turn off Nagle's algorithm since it degrades performance in most HttpClient scenarios.
            NoDelay = _httpConfig.GetValue("SocketNoDelay", true)
        };

        try
        {
            await socket.ConnectAsync(addresses.ToArray(), context.DnsEndPoint.Port, cancellationToken);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
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
    
    private struct ResourceRecordComparer: IEqualityComparer<ResourceRecord>
    {

        public bool Equals(ResourceRecord x, ResourceRecord y)
        {
            return x.Type == y.Type && x.Class == y.Class && x.DomainName.Equals(y.DomainName);
        }

        public int GetHashCode(ResourceRecord obj)
        {
            return HashCode.Combine(obj.DomainName, (int)obj.Type, (int)obj.Class);
        }
    }
}