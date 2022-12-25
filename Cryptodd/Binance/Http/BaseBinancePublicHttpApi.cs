using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Cryptodd.Binance.Http.Options;
using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.Binance.Json;
using Cryptodd.Binance.Models;
using Cryptodd.Binance.Models.Json;
using Cryptodd.Http;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.Binance.Http;

public abstract partial class BaseBinancePublicHttpApi<
    TOptions,
    TInternalBinanceRateLimiter
>
    where TOptions : BaseBinancePublicHttpApiOptions
    where TInternalBinanceRateLimiter : class, IInternalBinanceRateLimiter
{
    protected ILogger Logger { get; }

    protected BaseBinancePublicHttpApi(HttpClient httpClient, ILogger logger, IConfiguration configuration,
        IUriRewriteService uriRewriteService, TInternalBinanceRateLimiter internalRateLimiter)
    {
        Logger = logger.ForContext(GetType());
        HttpClient = httpClient;
        Configuration = configuration;
        UriRewriteService = uriRewriteService;
        InternalRateLimiter = internalRateLimiter;
        OptionsLazy = new Lazy<TOptions>(OptionsValueFactory);
        JsonSerializerOptions = new Lazy<JsonSerializerOptions>(CreateJsonSerializerOptions);
    }

    protected HttpClient HttpClient { get; }

    protected IConfiguration Configuration { get; }
    protected IUriRewriteService UriRewriteService { get; }
    protected Lazy<TOptions> OptionsLazy { get; set; }
    protected TInternalBinanceRateLimiter InternalRateLimiter { get; }

    protected JsonObject ExchangeInfo { get; set; } = new();
    protected abstract IConfigurationSection Section { get; }
    public TOptions Options => OptionsLazy.Value;
    public IBinanceRateLimiter RateLimiter => InternalRateLimiter;

    protected Lazy<JsonSerializerOptions> JsonSerializerOptions { get; init; }

    protected JsonObject? GetCachedExchangeInfo() => ExchangeInfo.Count > 0 ? ExchangeInfo : null;

    protected abstract TOptions OptionsValueFactory();

    protected virtual void SetupBaseAddress(string baseAddress)
    {
        if (!string.IsNullOrWhiteSpace(HttpClient.BaseAddress?.ToString()))
        {
            return;
        }

        try
        {
            HttpClient.BaseAddress = new Uri(baseAddress);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        var rewriteTask = UriRewriteService.Rewrite(HttpClient.BaseAddress).AsTask();
        rewriteTask.Wait();
        if (rewriteTask.IsCompleted)
        {
            HttpClient.BaseAddress = rewriteTask.Result;
        }
    }

    protected ValueTask<Uri> UriCombine(string url)
    {
        var uri =
            new UriBuilder(Section.GetValue("Url",
                    (string.IsNullOrWhiteSpace(Options.BaseAddress) ? HttpClient.BaseAddress?.ToString() : Options.BaseAddress) ?? Options.BaseAddress)!)
                .WithPathSegment(url)
                .Uri;
        return UriRewriteService.Rewrite(uri);
    }

    protected virtual JsonSerializerOptions CreateJsonSerializerOptions()
    {
        var res = new JsonSerializerOptions
            { NumberHandling = JsonNumberHandling.AllowReadingFromString, PropertyNameCaseInsensitive = true };
        res.Converters.Add(new BinancePriceQuantityEntryJsonConverter());
        return res;
    }

    # region Default HTTP API Get impl

    protected async Task<JsonObject> DoGetExchangeInfoAsync<TCallOptions>(TCallOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCallOptions : class, IBinancePublicHttpApiCallOptions, new()
    {
        options ??= new TCallOptions();
        var serializerOptions = options.JsonSerializerOptions ?? JsonSerializerOptions.Value;
        var uri = await UriCombine(options.Url);
        var weight = options.ComputeWeight(1.0);
        using var registration = await InternalRateLimiter.WaitForSlot(uri, weight, cancellationToken);

        var res = (await GetFromJsonAsync<JsonObject>(HttpClient, uri,
            serializerOptions,
            cancellationToken))!;

        registration.SetRegistrationDate();

        static bool TryGetServerTime<T>(T? o, out long serverTime) where T : JsonNode
        {
            serverTime = default;
            return o?["serverTime"] is JsonValue value && value.TryGetValue(out serverTime);
        }

        // ReSharper disable once InvertIf
        if (TryGetServerTime(res, out var newTime))
        {
            if (!TryGetServerTime(ExchangeInfo, out var oldServerTime) || newTime >= oldServerTime)
            {
                ExchangeInfo = res;
            }
        }

        return res;
    }

    protected async Task<BinanceHttpOrderbook> DotGetOrderbook<TCallOptions>(string symbol,
        int limit,
        TCallOptions? options = null,
        CancellationToken cancellationToken = default)
        where TCallOptions : class, IBinancePublicHttpApiCallOptions, new()
    {
        options ??= new TCallOptions();
        var uri = await UriCombine(options.Url);
        uri = new UriBuilder(uri).WithParameter("symbol", symbol)
            .WithParameter("limit", limit.ToString(CultureInfo.InvariantCulture)).Uri;
        var serializerOptions = options.JsonSerializerOptions ?? JsonSerializerOptions.Value;
        var date = DateTimeOffset.Now;
        BinanceHttpOrderbook res;
        var weight = options.ComputeWeight(limit);
        using var registration = await InternalRateLimiter.WaitForSlot(uri, weight, cancellationToken);
        using (AddResponseCallbacks(message => date = message.Headers.Date ?? DateTimeOffset.Now))
        {
            res = await GetFromJsonAsync<BinanceHttpOrderbook>(HttpClient, uri, serializerOptions, cancellationToken);
            registration.SetRegistrationDate();
        }

        return res with { DateTime = date };
    }

    protected async Task<List<string>> DoListSymbols<TCallOptions>(bool useCache = false, bool checkStatus = false,
        CancellationToken cancellationToken = default)
        where TCallOptions : class, IBinancePublicHttpApiCallOptions, new()
    {
        JsonObject exchangeInfo;
        if (useCache)
        {
            exchangeInfo = GetCachedExchangeInfo() ??
                           await DoGetExchangeInfoAsync<TCallOptions>(cancellationToken: cancellationToken)
                               .ConfigureAwait(false);
        }
        else
        {
            exchangeInfo = await DoGetExchangeInfoAsync<TCallOptions>(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        List<string> res = new();
        BinanceHttpApiHelper.ParseSymbols(ref res, exchangeInfo, checkStatus);
        return res;
    }

    #endregion
}