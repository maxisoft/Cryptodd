using System.Globalization;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Cryptodd.Ftx.Models;
using Cryptodd.Ftx.Models.Json;
using Cryptodd.Http;
using Cryptodd.IoC;
using Cryptodd.Json;
using Cryptodd.Json.Converters;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Ftx;

public interface IFtxPublicHttpApi: IDisposable
{
    public Task<PooledList<Future>> GetAllFuturesAsync(CancellationToken cancellationToken = default);

    Task<ArrayList<FundingRate>> GetAllFundingRatesAsync(DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null, CancellationToken cancellationToken = default);

    Task<ApiFutureStats?> GetFuturesStatsAsync(string futureName, CancellationToken cancellationToken = default);

    Task<PooledList<Market>> GetAllMarketsAsync(CancellationToken cancellationToken = default);
    Task<PooledList<FtxTrade>> GetTradesAsync(string market, CancellationToken cancellationToken = default);
    Task<PooledList<FtxTrade>> GetTradesAsync(string market, long startTime, long endTime,
        CancellationToken cancellationToken = default);
    
    bool DisposeHttpClient { get; set; }
}

public class FtxPublicHttpApi : IFtxPublicHttpApi, INoAutoRegister
{
    internal const int TradeDefaultCapacity = 8 << 10;
    internal const int MarketDefaultCapacity = 2048;
    internal const int FutureDefaultCapacity = 1024;
    private static readonly JsonSerializerOptions JsonSerializerOptions = CreateJsonSerializerOptions();
    private readonly HttpClient _httpClient;
    private readonly IUriRewriteService _uriRewriteService;


    public FtxPublicHttpApi(HttpClient httpClient, IUriRewriteService uriRewriteService)
    {
        if (string.IsNullOrWhiteSpace(httpClient.BaseAddress?.ToString()))
        {
            var rewriteTask = uriRewriteService.Rewrite(new Uri("https://ftx.com/api/")).AsTask();
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

    public async Task<PooledList<Future>> GetAllFuturesAsync(CancellationToken cancellationToken = default)
    {
        var uri = new UriBuilder($"{_httpClient.BaseAddress}futures").Uri;
        uri = await _uriRewriteService.Rewrite(uri);
        return (await _httpClient.GetFromJsonAsync<ResponseEnvelope<PooledList<Future>>>(uri,
            JsonSerializerOptions,
            cancellationToken)).Result ?? new PooledList<Future>();
    }

    public async Task<ArrayList<FundingRate>> GetAllFundingRatesAsync(DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null, CancellationToken cancellationToken = default)
    {
        var builder = new UriBuilder($"{_httpClient.BaseAddress}funding_rates");
        if (startTime.HasValue)
        {
            builder = builder.WithParameter("start_time",
                startTime.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        }

        if (endTime.HasValue)
        {
            builder = builder.WithParameter("end_time",
                endTime.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        }

        var uri = builder.Uri;
        uri = await _uriRewriteService.Rewrite(uri);
        return (await _httpClient.GetFromJsonAsync<ResponseEnvelope<ArrayList<FundingRate>>>(uri,
            JsonSerializerOptions,
            cancellationToken)).Result ?? new ArrayList<FundingRate>();
    }

    public async Task<ApiFutureStats?> GetFuturesStatsAsync(string futureName,
        CancellationToken cancellationToken = default)
    {
        var uri = new UriBuilder($"{_httpClient.BaseAddress}futures/{Uri.EscapeDataString(futureName)}/stats").Uri;
        uri = await _uriRewriteService.Rewrite(uri);
        return (await _httpClient.GetFromJsonAsync<ResponseEnvelope<ApiFutureStats>>(uri,
            JsonSerializerOptions,
            cancellationToken)).Result;
    }

    private static JsonSerializerOptions CreateJsonSerializerOptions()
    {
        var res = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        res.Converters.Add(new FtxTradeConverter());
        res.Converters.Add(new PooledListConverter<FtxTrade>() {DefaultCapacity = TradeDefaultCapacity});
        res.Converters.Add(new PooledListConverter<Market>() {DefaultCapacity = MarketDefaultCapacity});
        res.Converters.Add(new PooledListConverter<Future>() {DefaultCapacity = FutureDefaultCapacity});
        return res;
    }

    public async Task<PooledList<Market>> GetAllMarketsAsync(CancellationToken cancellationToken = default)
    {
        var uri = new UriBuilder($"{_httpClient.BaseAddress}markets").Uri;
        uri = await _uriRewriteService.Rewrite(uri);
        return (await _httpClient.GetFromJsonAsync<ResponseEnvelope<PooledList<Market>>>(uri,
            JsonSerializerOptions,
            cancellationToken)).Result ?? new PooledList<Market>();
    }

    public async Task<PooledList<FtxTrade>> GetTradesAsync(string market, CancellationToken cancellationToken = default)
    {
        // Duno if Escaping is right in the long term as both
        // BTC/USDT
        // BTC%2FUSDT
        // works
        var uri = new UriBuilder($"{_httpClient.BaseAddress}markets/{Uri.EscapeDataString(market)}/trades").Uri;
        uri = await _uriRewriteService.Rewrite(uri);
        return (await _httpClient.GetFromJsonAsync<ResponseEnvelope<PooledList<FtxTrade>>>(uri,
            JsonSerializerOptions,
            cancellationToken)).Result ?? new PooledList<FtxTrade>();
    }

    public async Task<PooledList<FtxTrade>> GetTradesAsync(string market, long startTime,
        long endTime,
        CancellationToken cancellationToken = default)
    {
        var uri = new UriBuilder($"{_httpClient.BaseAddress}markets/{Uri.EscapeDataString(market)}/trades")
            .WithParameter("start_time", startTime.ToString(CultureInfo.InvariantCulture))
            .WithParameter("end_time", endTime.ToString(CultureInfo.InvariantCulture))
            .Uri;
        uri = await _uriRewriteService.Rewrite(uri);
        return (await _httpClient.GetFromJsonAsync<ResponseEnvelope<PooledList<FtxTrade>>>(uri,
            JsonSerializerOptions,
            cancellationToken)).Result ?? new PooledList<FtxTrade>();
    }

    public bool DisposeHttpClient { get; set; }

    public void Dispose()
    {
        if (DisposeHttpClient)
        {
            _httpClient.Dispose();
        }
        
        GC.SuppressFinalize(this);
    }
}