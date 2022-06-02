using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cryptodd.Ftx.Models;
using Cryptodd.Http;
using Maxisoft.Utils.Collections.Lists;

namespace Cryptodd.Ftx;

public interface IFtxPublicHttpApi
{
    public Task<List<Future>> GetAllFuturesAsync(CancellationToken cancellationToken = default);

    Task<ArrayList<FundingRate>> GetAllFundingRatesAsync(DateTimeOffset? startTime = null,
        DateTimeOffset? endTime = null, CancellationToken cancellationToken = default);
}

public class FtxPublicHttpApi : IFtxPublicHttpApi
{
    private readonly HttpClient _httpClient;
    private readonly IUriRewriteService _uriRewriteService;

    private static readonly JsonSerializerOptions JsonSerializerOptions = CreateJsonSerializerOptions();

    private static JsonSerializerOptions CreateJsonSerializerOptions()
    {
        var res = new JsonSerializerOptions()
            { PropertyNameCaseInsensitive = true };
        return res;
    }


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

    public async Task<List<Future>> GetAllFuturesAsync(CancellationToken cancellationToken = default)
    {
        var uri = new UriBuilder($"{_httpClient.BaseAddress}futures").Uri;
        uri = await _uriRewriteService.Rewrite(uri);
        return (await _httpClient.GetFromJsonAsync<ResponseEnvelope<List<Future>>>(uri,
            JsonSerializerOptions,
            cancellationToken)).Result ?? new List<Future>();
    }
    
    public async Task<List<Market>> GetAllMarketsAsync(CancellationToken cancellationToken = default)
    {
        var uri = new UriBuilder($"{_httpClient.BaseAddress}markets").Uri;
        uri = await _uriRewriteService.Rewrite(uri);
        return (await _httpClient.GetFromJsonAsync<ResponseEnvelope<List<Market>>>(uri,
            JsonSerializerOptions,
            cancellationToken)).Result ?? new List<Market>();
    }
    
    public async Task<ArrayList<FundingRate>> GetAllFundingRatesAsync(DateTimeOffset? startTime = null, DateTimeOffset? endTime = null, CancellationToken cancellationToken = default)
    {
        var builder = new UriBuilder($"{_httpClient.BaseAddress}funding_rates");
        if (startTime.HasValue)
        {
            builder = builder.WithParameter("start_time", startTime.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        }

        if (endTime.HasValue)
        {
            builder = builder.WithParameter("end_time", endTime.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        }

        var uri = builder.Uri;
        uri = await _uriRewriteService.Rewrite(uri);
        return (await _httpClient.GetFromJsonAsync<ResponseEnvelope<ArrayList<FundingRate>>>(uri,
            JsonSerializerOptions,
            cancellationToken)).Result ?? new ArrayList<FundingRate>();
    }
}