using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Cryptodd.Http;
using Cryptodd.IoC;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Bitfinex;

public interface IBitfinexPublicHttpApi : IService
{
    ValueTask<List<string>> GetAllPairs(CancellationToken cancellationToken);
}

public class BitfinexPublicHttpApi : IBitfinexPublicHttpApi
{
    private readonly HttpClient _httpClient;
    private readonly IUriRewriteService _uriRewriteService;

    private readonly ConcurrentDictionary<string, BitfinexRateLimiter> _rateLimiters =
        new ConcurrentDictionary<string, BitfinexRateLimiter>();

    public BitfinexPublicHttpApi(HttpClient httpClient, IUriRewriteService uriRewriteService)
    {
        if (string.IsNullOrWhiteSpace(httpClient.BaseAddress?.ToString()))
        {
            var rewriteTask = uriRewriteService.Rewrite(new Uri("https://api-pub.bitfinex.com/")).AsTask();
            if (rewriteTask.IsCompleted)
            {
                httpClient.BaseAddress = rewriteTask.Result;
            }
            else
            {
                rewriteTask.ContinueWith(task => httpClient.BaseAddress = task.Result);
            }
        }

        _httpClient = httpClient;
        _uriRewriteService = uriRewriteService;
    }


    public async ValueTask<List<string>> GetAllPairs(CancellationToken cancellationToken)
    {
        var uri = new UriBuilder($"{_httpClient.BaseAddress}v2/conf/pub:list:pair:exchange").Uri;
        uri = await _uriRewriteService.Rewrite(uri);
        var rateLimiter = _rateLimiters.GetOrAdd("conf", _ => new BitfinexRateLimiter() { MaxRequestPerMinutes = 90 });
        using var helper = rateLimiter.Helper();
        await helper.Wait(cancellationToken).ConfigureAwait(false);
        return (await _httpClient.GetFromJsonAsync<List<string>[]>(uri,
            cancellationToken))?[0] ?? new List<string>();
    }
}