using System.Collections;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Cryptodd.Bitfinex.Models;
using Cryptodd.Bitfinex.Models.Json;
using Cryptodd.Ftx.Models;
using Cryptodd.Ftx.Models.Json;
using Cryptodd.Http;
using Cryptodd.IoC;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Bitfinex;

public interface IBitfinexPublicHttpApi : IBitfinexPairProvider, IService
{
}

public class BitfinexPublicHttpApi : IBitfinexPublicHttpApi, INoAutoRegister
{
    private readonly HttpClient _httpClient;
    private readonly IUriRewriteService _uriRewriteService;

    private readonly ConcurrentDictionary<string, BitfinexRateLimiter> _rateLimiters =
        new ConcurrentDictionary<string, BitfinexRateLimiter>();
    
    private static readonly JsonSerializerOptions JsonSerializerOptions = CreateJsonSerializerOptions();

    private static JsonSerializerOptions CreateJsonSerializerOptions()
    {
        var res = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        res.Converters.Add(new DerivativeStatusConverter());
        res.Converters.Add(new PooledListConverter<DerivativeStatus>() {DefaultCapacity = 1024});
        return res;
    }

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
    
    public async ValueTask<ArrayList<string>> GetAllPairs(CancellationToken cancellationToken)
    {
        var uri = new UriBuilder($"{_httpClient.BaseAddress}v2/conf/pub:list:pair:exchange").Uri;
        uri = await _uriRewriteService.Rewrite(uri);
        var rateLimiter = _rateLimiters.GetOrAdd("conf", _ => new BitfinexRateLimiter() { MaxRequestPerMinutes = 90 });
        using var helper = rateLimiter.Helper();
        await helper.Wait(cancellationToken).ConfigureAwait(false);
        var res = (await _httpClient.GetFromJsonAsync<string[][]>(uri,
            cancellationToken))?[0];
        return res is not null ? new ArrayList<string>(res) : new ArrayList<string>();
    }

    public async ValueTask<PooledList<DerivativeStatus>> GetDerivativeStatus(CancellationToken cancellationToken)
    {
        var uri = new UriBuilder($"{_httpClient.BaseAddress}v2/status/deriv")
            .WithParameter("keys", "ALL")
            .Uri;
        uri = await _uriRewriteService.Rewrite(uri);
        var rateLimiter = _rateLimiters.GetOrAdd("conf", _ => new BitfinexRateLimiter() { MaxRequestPerMinutes = 90 });
        using var helper = rateLimiter.Helper();
        await helper.Wait(cancellationToken).ConfigureAwait(false);
        return (await _httpClient.GetFromJsonAsync<PooledList<DerivativeStatus>>(uri,
            JsonSerializerOptions,
            cancellationToken)) ?? new PooledList<DerivativeStatus>();
    }
}