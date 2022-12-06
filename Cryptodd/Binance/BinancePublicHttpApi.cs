using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Cryptodd.Binance.Models;
using Cryptodd.Binance.Models.Json;
using Cryptodd.Ftx.Models.Json;
using Cryptodd.Http;
using Cryptodd.IoC;
using Maxisoft.Utils.Collections.LinkedLists;
using Maxisoft.Utils.Collections.Lists.Specialized;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.Binance;

public enum BinancePublicHttpApiEndPoint
{
    None,
    ExchangeInfo,
    OrderBook
}

public class BinancePublicHttpApiOptions
{
    public string BaseAddress { get; set; } = "https://api.binance.com";
    public float UsedWeightMultiplier { get; set; } = 1.0f;
    public string UsedWeightHeaderName { get; set; } = "X-MBX-USED-WEIGHT-1M";
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

public class BinancePublicHttpApi : IBinancePublicHttpApi, INoAutoRegister
{
    public const int DefaultOrderbookLimit = 100;
    private readonly HttpClient _httpClient;

    private readonly IConfiguration _configuration;
    private readonly IUriRewriteService _uriRewriteService;
    private readonly Lazy<BinancePublicHttpApiOptions> _options;

    internal BinancePublicHttpApiOptions Options => _options.Value;

    private BinancePublicHttpApiOptions OptionsValueFactory()
    {
        var res = new BinancePublicHttpApiOptions();
        Section.Bind(res);
        SetupBaseAddress(res.BaseAddress);
        return res;
    }

    private JsonObject _exchangeInfo = new JsonObject();

    internal static readonly AsyncLocal<LinkedListAsIList<Action<HttpResponseMessage>>> HttpMessageCallbacks = new();

    internal sealed class RemoveCallbackOnDispose<T> : IDisposable
    {
        private LinkedListNode<Action<T>>? _node;

        public RemoveCallbackOnDispose(LinkedListNode<Action<T>>? node)
        {
            _node = node;
        }

        public void Dispose()
        {
            if (_node is null)
            {
                return;
            }

            lock (this)
            {
                _node?.List?.Remove(_node);
                _node = null;
            }
        }
    }

    internal static RemoveCallbackOnDispose<HttpResponseMessage> AddResponseCallbacks(
        Action<HttpResponseMessage> action)
    {
        HttpMessageCallbacks.Value ??= new LinkedListAsIList<Action<HttpResponseMessage>>();
        var node = HttpMessageCallbacks.Value.AddLast(action);
        return new RemoveCallbackOnDispose<HttpResponseMessage>(node);
    }

    public BinancePublicHttpApi(HttpClient httpClient, IConfiguration configuration,
        IUriRewriteService uriRewriteService)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _uriRewriteService = uriRewriteService;
        _options = new Lazy<BinancePublicHttpApiOptions>(OptionsValueFactory);
    }

    protected virtual void SetupBaseAddress(string baseAddress)
    {
        if (!string.IsNullOrWhiteSpace(_httpClient.BaseAddress?.ToString()))
        {
            return;
        }

        _httpClient.BaseAddress = new Uri(baseAddress);
        var rewriteTask = _uriRewriteService.Rewrite(_httpClient.BaseAddress).AsTask();
        if (rewriteTask.IsCompleted)
        {
            _httpClient.BaseAddress = rewriteTask.Result;
        }
        else
        {
            rewriteTask.ContinueWith(task => _httpClient.BaseAddress = task.Result);
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
        var res = (await GetFromJsonAsync<JsonObject>(_httpClient, await UriCombine(options.Url),
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
        var date = DateTimeOffset.Now;
        BinanceHttpOrderbook res;
        using (AddResponseCallbacks(message => date = message.Headers.Date ?? DateTimeOffset.Now))
        {
            res = await GetFromJsonAsync<BinanceHttpOrderbook>(_httpClient, uri, serializerOptions, cancellationToken);
        }

        return res with { DateTime = date };
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

    #region HttpClientJsonExtensions copy pasted code + adapted

    public Task<TValue?> GetFromJsonAsync<TValue>(HttpClient client, Uri? requestUri,
        JsonSerializerOptions? options, CancellationToken cancellationToken = default)
    {
        var taskResponse = client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return GetFromJsonAsyncCore<TValue>(taskResponse, options, cancellationToken);
    }

    private async Task<T?> GetFromJsonAsyncCore<T>(Task<HttpResponseMessage> taskResponse,
        JsonSerializerOptions? options, CancellationToken cancellationToken)
    {
        using var response = await taskResponse.ConfigureAwait(false);
        UpdateUsedWeight(response);
        var callbacks = HttpMessageCallbacks.Value;
        if (callbacks is not null)
        {
            foreach (var callback in callbacks)
            {
                callback(response);
            }
        }

        response.EnsureSuccessStatusCode();
        return await ReadFromJsonAsyncHelper<T>(response.Content, options, cancellationToken).ConfigureAwait(false);
    }

    private static Task<T?> ReadFromJsonAsyncHelper<T>(HttpContent content, JsonSerializerOptions? options,
        CancellationToken cancellationToken)
        => content.ReadFromJsonAsync<T>(options, cancellationToken);

    #endregion

    private void UpdateUsedWeight(HttpResponseMessage response)
    {
        var headers = response.Headers;
        var usedWeightFloat = 0.0;
        if (headers.TryGetValues(Options.UsedWeightHeaderName, out var usedWeights))
        {
            foreach (var usedWeightString in usedWeights)
            {
                if (long.TryParse(usedWeightString, out var usedWeight))
                {
                    usedWeightFloat = Math.Max(usedWeightFloat, usedWeight);
                }
            }
        }

        usedWeightFloat *= Options.UsedWeightMultiplier;
    }
}