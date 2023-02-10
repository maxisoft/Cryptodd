using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Cryptodd.Bitfinex.Http.Abstractions;
using Cryptodd.Bitfinex.Models;
using Cryptodd.Bitfinex.Models.Json;
using Cryptodd.Http.Abstractions;
using Cryptodd.IoC;
using Cryptodd.Json.Converters;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Collections.Lists.Specialized;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.Bitfinex.Http;

public interface IBitfinexPublicHttpApi : IBitfinexPairProvider, IService
{
    Task<PooledList<DerivativeStatus>> GetDerivativeStatus(CancellationToken cancellationToken);
}

public class BitfinexPublicHttpApiOptions
{
    public string BaseAddress { get; set; }= "https://api-pub.bitfinex.com/";
}

public enum SortOrder: sbyte
{
    None = 0,
    /// <summary>Nodes are sorted in ascending order. For example, if the numbers 1,2,3, and 4 are sorted in ascending order, they appear as 1,2,3,4.</summary>
    Ascending = 1,
    /// <summary>Nodes are sorted in descending order. For example, if the numbers 1,2,3, and 4 are sorted in descending order, they appear as, 4,3,2,1.</summary>
    Descending = -1
}

public class BitfinexPublicHttpApi : IBitfinexPublicHttpApi, INoAutoRegister
{
    private readonly IBitfinexHttpClientAbstraction _httpClient;
    private readonly BitfinexPublicHttpApiOptions _options = new();

    private readonly ConcurrentDictionary<string, BitfinexRateLimiter> _rateLimiters = new();

    private static readonly JsonSerializerOptions JsonSerializerOptions = CreateJsonSerializerOptions();

    private static JsonSerializerOptions CreateJsonSerializerOptions()
    {
        var res = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        res.Converters.Add(new DerivativeStatusConverter());
        res.Converters.Add(new PooledListConverter<DerivativeStatus>() { DefaultCapacity = 1024 });
        res.Converters.Add(new BitfinexTradeConverter());
        res.Converters.Add(new PooledListConverter<BitfinexTrade>() { DefaultCapacity = 10_000 });
        return res;
    }

    public BitfinexPublicHttpApi(IBitfinexHttpClientAbstraction httpClient, IConfiguration configuration)
    {
        configuration.GetSection("Bitfinex:Http").Bind(_options);

        _httpClient = httpClient;
    }

    public async Task<ArrayList<string>> GetAllPairs(CancellationToken cancellationToken)
    {
        var uri = new UriBuilder($"{_options.BaseAddress}v2/conf/pub:list:pair:exchange").Uri;
        var rateLimiter = _rateLimiters.GetOrAdd("conf", _ => new BitfinexRateLimiter() { MaxRequestPerMinutes = 90 });
        using var helper = rateLimiter.Helper();
        await helper.Wait(cancellationToken).ConfigureAwait(false);
        var res = (await _httpClient.GetFromJsonAsync<string[][]>(uri,
            JsonSerializerOptions,
            cancellationToken))?[0];
        return res is not null ? new ArrayList<string>(res) : new ArrayList<string>();
    }

    public async Task<PooledList<DerivativeStatus>> GetDerivativeStatus(CancellationToken cancellationToken)
    {
        var uri = new UriBuilder($"{_options.BaseAddress}v2/status/deriv")
            .WithParameter("keys", "ALL")
            .Uri;
        var rateLimiter = _rateLimiters.GetOrAdd("status", _ => new BitfinexRateLimiter() { MaxRequestPerMinutes = 30 });
        using var helper = rateLimiter.Helper();
        await helper.Wait(cancellationToken).ConfigureAwait(false);
        return (await _httpClient.GetFromJsonAsync<PooledList<DerivativeStatus>>(uri,
            JsonSerializerOptions,
            cancellationToken)) ?? new PooledList<DerivativeStatus>();
    }
    
    public async Task<PooledList<BitfinexTrade>> GetTrades(string symbol, int? limit = null, SortOrder sort = SortOrder.None, string? start = null, string? end = null, CancellationToken cancellationToken = default)
    {
        var uriBuilder = new UriBuilder($"{_options.BaseAddress}v2/trades/{symbol}/hist");
        if (limit is not null)
        {
            uriBuilder = uriBuilder.WithParameter("limit", limit.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (sort is not SortOrder.None)
        {
            uriBuilder = uriBuilder.WithParameter("sort", ((int) sort).ToString(CultureInfo.InvariantCulture));
        }
        
        if (start is not null)
        {
            uriBuilder = uriBuilder.WithParameter("start", start);
        }
        
        if (end is not null)
        {
            uriBuilder = uriBuilder.WithParameter("end", end);
        }
        
        var uri = uriBuilder.Uri;
        var rateLimiter = _rateLimiters.GetOrAdd("trades", _ => new BitfinexRateLimiter() { MaxRequestPerMinutes = 90 });
        using var helper = rateLimiter.Helper();
        await helper.Wait(cancellationToken).ConfigureAwait(false);
        return (await _httpClient.GetFromJsonAsync<PooledList<BitfinexTrade>>(uri,
            JsonSerializerOptions,
            cancellationToken)) ?? new PooledList<BitfinexTrade>();
    }
}