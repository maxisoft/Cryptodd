using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Cryptodd.Binance.Models;
using Cryptodd.Binance.Models.Json;
using Cryptodd.Ftx.Models.Json;
using Cryptodd.Http;
using Cryptodd.IoC;
using Maxisoft.Utils.Collections.Lists.Specialized;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.Binance;

public enum BinancePublicHttpApiEndPoint
{
    None,
    ExchangeInfo,
    OrderBook
}

public class BinancePublicHttpApiCallOptions
{
    public BinancePublicHttpApiEndPoint EndPoint { get; set; } = BinancePublicHttpApiEndPoint.None;
    public int BaseWeight { get; set; } = -1;
    public string Url { get; set; } = "";
    public JsonSerializerOptions? JsonSerializerOptions { get; set; }

    public virtual int ComputeWeigh(double factor) => BaseWeight;
}

public class BinancePublicHttpApiCallOptionsExchangeInfo : BinancePublicHttpApiCallOptions
{
    public const string DefaultUrl = "/api/v3/exchangeInfo";

    public BinancePublicHttpApiCallOptionsExchangeInfo()
    {
        EndPoint = BinancePublicHttpApiEndPoint.ExchangeInfo;
        BaseWeight = 10;
        Url = DefaultUrl;
    }
}

public class BinancePublicHttpApiCallOptionsOrderBook : BinancePublicHttpApiCallOptions
{
    public const string DefaultUrl = "/api/v3/depth";

    public BinancePublicHttpApiCallOptionsOrderBook()
    {
        EndPoint = BinancePublicHttpApiEndPoint.OrderBook;
        BaseWeight = -1;
        Url = DefaultUrl;
    }

    public override int ComputeWeigh(double factor)
    {
        var weight = BaseWeight;
        if (weight >= 0)
        {
            return weight;
        }

        weight += 1;
        weight += factor switch
        {
            > 1000 => 50,
            > 500 => 10,
            > 100 => 5,
            _ => -1
        };
        return weight;
    }
}

public class BinancePublicHttpApi : INoAutoRegister
{
    public const int DefaultOrderbookLimit = 100;
    public const string BinanceBaseAddress = "https://api.binance.com";
    private readonly HttpClient _httpClient;

    private readonly IConfiguration _configuration;
    private readonly IUriRewriteService _uriRewriteService;

    private JsonObject _exchangeInfo = new JsonObject();

    public BinancePublicHttpApi(HttpClient httpClient, IConfiguration configuration,
        IUriRewriteService uriRewriteService)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _uriRewriteService = uriRewriteService;

        if (string.IsNullOrWhiteSpace(httpClient.BaseAddress?.ToString()))
        {
            httpClient.BaseAddress = new Uri(BinanceBaseAddress);
            var rewriteTask = uriRewriteService.Rewrite(new Uri(BinanceBaseAddress)).AsTask();
            if (rewriteTask.IsCompleted)
            {
                httpClient.BaseAddress = rewriteTask.Result;
            }
            else
            {
                rewriteTask.ContinueWith(task => httpClient.BaseAddress = task.Result);
            }
        }
    }

    protected virtual IConfigurationSection Section => _configuration.GetSection("Binance:Http");

    private ValueTask<Uri> UriCombine(string url)
    {
        var uri =
            new UriBuilder(Section.GetValue("Url", _httpClient.BaseAddress!.ToString())!).WithPathSegment(url)
                .Uri;
        return _uriRewriteService.Rewrite(uri);
    }

    public async Task<JsonObject> GetExchangeInfoAsync(BinancePublicHttpApiCallOptionsExchangeInfo? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new BinancePublicHttpApiCallOptionsExchangeInfo();
        var serializerOptions = options.JsonSerializerOptions ?? JsonSerializerOptions.Value;
        var res = (await _httpClient.GetFromJsonAsync<JsonObject>(await UriCombine(options.Url),
            serializerOptions,
            cancellationToken))!;

        static bool TryGetServerTime<T>(T? o, out long serverTime) where T : JsonNode
        {
            serverTime = default;
            return o?["serverTime"] is JsonValue value && value.TryGetValue(out serverTime);
        }

        // ReSharper disable once InvertIf
        if (TryGetServerTime(res, out var newTime))
        {
            if (!TryGetServerTime(_exchangeInfo, out var oldServerTime) || newTime >= oldServerTime)
            {
                _exchangeInfo = res;
            }
        }

        return res;
    }

    internal JsonObject? GetCachedExchangeInfo() => _exchangeInfo.Count > 0 ? _exchangeInfo : null;

    public async Task<BinanceHttpOrderbook> GetOrderbook(string symbol, int limit = DefaultOrderbookLimit,
        BinancePublicHttpApiCallOptionsOrderBook? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new BinancePublicHttpApiCallOptionsOrderBook();
        var uri = await UriCombine(options.Url);
        uri = new UriBuilder(uri).WithParameter("symbol", symbol)
            .WithParameter("limit", limit.ToString(CultureInfo.InvariantCulture)).Uri;
        var serializerOptions = options.JsonSerializerOptions ?? JsonSerializerOptions.Value;
        var res = await _httpClient.GetFromJsonAsync<BinanceHttpOrderbook>(uri, serializerOptions, cancellationToken);
        return res;
    }

    private static JsonSerializerOptions CreateJsonSerializerOptions()
    {
        var res = new JsonSerializerOptions
            { NumberHandling = JsonNumberHandling.AllowReadingFromString, PropertyNameCaseInsensitive = true };
        res.Converters.Add(new BinancePriceQuantityEntryConverter());
        res.Converters.Add(new PooledListConverter<BinancePriceQuantityEntry<double>>()
            { DefaultCapacity = DefaultOrderbookLimit });
        return res;
    }

    private static readonly Lazy<JsonSerializerOptions> JsonSerializerOptions = new(CreateJsonSerializerOptions);
}