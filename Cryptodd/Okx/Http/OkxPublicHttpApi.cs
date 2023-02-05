using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Cryptodd.Http;
using Cryptodd.Http.Abstractions;
using Cryptodd.Http.Limiters;
using Cryptodd.IoC;
using Cryptodd.Json;
using Cryptodd.Json.Converters;
using Cryptodd.Okx.Http.Abstractions;
using Cryptodd.Okx.Json;
using Cryptodd.Okx.Models;
using Cryptodd.Okx.Models.HttpResponse;
using Cryptodd.Okx.Models.RubikStats;
using Maxisoft.Utils.Collections.Lists.Specialized;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.Okx.Http;

public interface IOkxPublicHttpApi : IOkxInstrumentIdsProvider
{
    public Task<OkxHttpGetOpenInterestResponse> GetOpenInterest(OkxInstrumentType instrumentType,
        string? underlying = null,
        string? instrumentFamily = null, CancellationToken cancellationToken = default);

    public Task<OkxHttpGetInstrumentsResponse> GetInstruments(OkxInstrumentType instrumentType,
        string? underlying = null,
        string? instrumentFamily = null, string? instrumentId = null, CancellationToken cancellationToken = default);

    public Task<OkxHttpGetTickersResponse> GetTickers(OkxInstrumentType instrumentType, string? underlying = null,
        string? instrumentFamily = null, CancellationToken cancellationToken = default);

    public Task<OkxHttpGetMarkPriceResponse> GetMarkPrices(OkxInstrumentType instrumentType,
        string? underlying = null,
        string? instrumentFamily = null, string? instrumentId = null, CancellationToken cancellationToken = default);

    public Task<OkxHttpGetOptionMarketDataResponse> GetOptionMarketData(string? underlying = null,
        string? instrumentFamily = null, string? expiryTime = null, CancellationToken cancellationToken = default);
}

public interface IOkxPublicHttpRubikApi
{
    Task<OkxHttpGetTakerVolumeResponse> GetTakerVolume(string currency, OkxInstrumentType instrumentType,
        string? begin = null, string? end = null, string period = "5m",
        CancellationToken cancellationToken = default);

    Task<OkxHttpGetMarginLendingRatioResponse> GetMarginLendingRatio(string currency,
        string? begin = null, string? end = null, string period = "5m",
        CancellationToken cancellationToken = default);

    Task<OkxHttpGetLongShortRatioResponse> GetLongShortRatio(string currency,
        string? begin = null, string? end = null, string period = "5m",
        CancellationToken cancellationToken = default);

    Task<OkxHttpGetContractsOpenInterestAndVolumeVolumeResponse> GetContractsOpenInterestAndVolume(
        string currency,
        string? begin = null, string? end = null, string period = "5m",
        CancellationToken cancellationToken = default);
}

public class OkxPublicHttpApi : IOkxPublicHttpApi, IOkxPublicHttpRubikApi, IOkxInstrumentIdsProvider, IService
{
    internal static readonly StringPool StringPool = new(10 << 10);
    private readonly IOkxHttpClientAbstraction _client;

    private readonly Lazy<JsonSerializerOptions> _jsonSerializerOptions;
    private readonly OkxPublicHttpApiOptions _options = new();
    private readonly OkxHttpUrlBuilder _urlBuilder;


    public OkxPublicHttpApi(IOkxHttpClientAbstraction client, IConfiguration configuration)
    {
        _client = client;
        configuration.GetSection("Okx:Http").Bind(_options);
        _urlBuilder = new OkxHttpUrlBuilder(_options);
        _jsonSerializerOptions = new Lazy<JsonSerializerOptions>(CreateJsonSerializerOptions);
    }

    public async Task<List<string>> ListInstrumentIds(OkxInstrumentType instrumentType, string? underlying = null,
        string? instrumentFamily = null, string? instrumentId = null, string? expectedState = "live",
        CancellationToken cancellationToken = default)
    {
        var resp = await GetRawInstruments(instrumentType, underlying, instrumentFamily, instrumentId,
                cancellationToken)
            .ConfigureAwait(false);

        IEnumerable<string> Generator()
        {
            if (resp.TryGetPropertyValue("data", out var data) && data is JsonArray dataArray)
            {
                foreach (var instrumentInfo in dataArray)
                {
                    // ReSharper disable once InvertIf
                    if (instrumentInfo is JsonObject instrumentInfoObj &&
                        instrumentInfoObj.TryGetPropertyValue("instId", out var instId) && instId is JsonValue value &&
                        value.TryGetValue(out string? instIdString) && !string.IsNullOrEmpty(instIdString))
                    {
                        if (string.IsNullOrEmpty(expectedState) ||
                            (instrumentInfoObj.TryGetPropertyValue("state", out var state) &&
                             state is JsonValue stateValue && stateValue.TryGetValue(out string? stateString) &&
                             stateString == expectedState))
                        {
                            yield return instIdString;
                        }
                    }
                }
            }
        }

        return new List<string>(Generator());
    }

    public async Task<JsonObject> GetRawInstruments(OkxInstrumentType instrumentType, string? underlying = null,
        string? instrumentFamily = null, string? instrumentId = null, CancellationToken cancellationToken = default)
    {
        var instrumentTypeString = instrumentType.ToHttpString();
        var url = await _urlBuilder.UriCombine(_options.GetInstrumentsUrl, instrumentType: instrumentTypeString,
                underlying: underlying, instrumentFamily: instrumentFamily, instrumentId: instrumentId,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using (_client.UseLimiter<InstrumentsHttpOkxLimiter>(instrumentTypeString, "Http:ListInstruments"))
        {
            return await _client.GetFromJsonAsync<JsonObject>(url, _jsonSerializerOptions.Value, cancellationToken)
                .ConfigureAwait(false) ?? new JsonObject();
        }
    }

    public async Task<OkxHttpGetInstrumentsResponse> GetInstruments(OkxInstrumentType instrumentType,
        string? underlying = null,
        string? instrumentFamily = null, string? instrumentId = null, CancellationToken cancellationToken = default)
    {
        var instrumentTypeString = instrumentType.ToHttpString();
        var url = await _urlBuilder.UriCombine(_options.GetInstrumentsUrl, instrumentType: instrumentTypeString,
                underlying: underlying, instrumentFamily: instrumentFamily, instrumentId: instrumentId,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using (_client.UseLimiter<InstrumentsHttpOkxLimiter>(instrumentTypeString, "Http:ListInstruments"))
        {
            return await _client
                .GetFromJsonAsync<OkxHttpGetInstrumentsResponse>(url, _jsonSerializerOptions.Value, cancellationToken)
                .ConfigureAwait(false) ?? new OkxHttpGetInstrumentsResponse(-1, "", new List<OkxHttpInstrumentInfo>());
        }
    }


    public async Task<OkxHttpGetTickersResponse> GetTickers(OkxInstrumentType instrumentType, string? underlying = null,
        string? instrumentFamily = null, CancellationToken cancellationToken = default)
    {
        var instrumentTypeString = instrumentType.ToHttpString();
        var url = await _urlBuilder.UriCombine(_options.GetTickersUrl, instrumentTypeString,
                underlying, instrumentFamily, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using (_client.UseLimiter<TickersHttpOkxLimiter>("", "Http:GetTickers"))
        {
            return await _client
                .GetFromJsonAsync<OkxHttpGetTickersResponse>(url, _jsonSerializerOptions.Value, cancellationToken)
                .ConfigureAwait(false) ?? new OkxHttpGetTickersResponse(-1, "", new List<OkxHttpTickerInfo>());
        }
    }


    public async Task<OkxHttpGetMarkPriceResponse> GetMarkPrices(OkxInstrumentType instrumentType,
        string? underlying = null,
        string? instrumentFamily = null, string? instrumentId = null, CancellationToken cancellationToken = default)
    {
        var instrumentTypeString = instrumentType.ToHttpString();
        var url = await _urlBuilder.UriCombine(_options.GetMarkPricesUrl, instrumentType: instrumentTypeString,
                underlying: underlying, instrumentFamily: instrumentFamily, instrumentId: instrumentId,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using (_client.UseLimiter<MarkPricesHttpOkxLimiter>(instrumentId ?? "", "Http:GetMarkPrices"))
        {
            return await _client
                .GetFromJsonAsync<OkxHttpGetMarkPriceResponse>(url, _jsonSerializerOptions.Value, cancellationToken)
                .ConfigureAwait(false) ?? new OkxHttpGetMarkPriceResponse(-1, "", new List<OkxHttpMarkPrice>());
        }
    }

    public async Task<OkxHttpGetOpenInterestResponse> GetOpenInterest(OkxInstrumentType instrumentType,
        string? underlying = null,
        string? instrumentFamily = null, CancellationToken cancellationToken = default)
    {
        var instrumentTypeString = instrumentType.ToHttpString();
        var url = await _urlBuilder.UriCombine(_options.GetOpenInterestUrl, instrumentTypeString,
                underlying, instrumentFamily, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using (_client.UseLimiter<OpenInterestHttpOkxLimiter>(instrumentTypeString, "Http:GetOpenInterest"))
        {
            return await _client
                       .GetFromJsonAsync<OkxHttpGetOpenInterestResponse>(url, _jsonSerializerOptions.Value,
                           cancellationToken)
                       .ConfigureAwait(false) ??
                   new OkxHttpGetOpenInterestResponse(-1, "", new PooledList<OkxHttpOpenInterest>());
        }
    }


    public async Task<OkxHttpGetOptionMarketDataResponse> GetOptionMarketData(string? underlying = null,
        string? instrumentFamily = null, string? expiryTime = null, CancellationToken cancellationToken = default)
    {
        var url = await _urlBuilder.UriCombine(_options.GetOptionMarketDataUrl,
                underlying: underlying, instrumentFamily: instrumentFamily, expiryTime: expiryTime,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using (_client.UseLimiter<OptionMarketDataHttpOkxLimiter>(underlying ?? "", "Http:GetOptionMarketData"))
        {
            return await _client
                       .GetFromJsonAsync<OkxHttpGetOptionMarketDataResponse>(url, _jsonSerializerOptions.Value,
                           cancellationToken)
                       .ConfigureAwait(false) ??
                   new OkxHttpGetOptionMarketDataResponse(-1, "", new List<OkxHttpOptionSummary>());
        }
    }

    public async Task<OkxHttpGetFundingRateResponse> GetFundingRate(string instrumentId,
        CancellationToken cancellationToken = default)
    {
        var url = await _urlBuilder.UriCombine(_options.GetFundingRateUrl, instrumentId: instrumentId,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using (_client.UseLimiter<FundingRatetHttpOkxLimiter>(instrumentId, "Http:GetFundingRate"))
        {
            return await _client
                       .GetFromJsonAsync<OkxHttpGetFundingRateResponse>(url, _jsonSerializerOptions.Value,
                           cancellationToken)
                       .ConfigureAwait(false) ??
                   new OkxHttpGetFundingRateResponse(-1, "", new OneItemList<OkxHttpFundingRate>());
        }
    }

    public async Task<OkxHttpGetTakerVolumeResponse> GetTakerVolume(string currency, OkxInstrumentType instrumentType,
        string? begin = null, string? end = null, string period = "5m",
        CancellationToken cancellationToken = default)
    {
        var url = await _urlBuilder.UriCombine(_options.GetTakerVolume,
                instrumentType, ccy: currency,
                begin: begin, end: end, period: period,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using (_client.UseLimiter<RubikTakerVolumeHttpOkxLimiter>("", "Http:GetTakerVolume"))
        {
            return await _client
                       .GetFromJsonAsync<OkxHttpGetTakerVolumeResponse>(url, _jsonSerializerOptions.Value,
                           cancellationToken)
                       .ConfigureAwait(false) ??
                   new OkxHttpGetTakerVolumeResponse(-1, "", new List<OkxHttpRubikTakerVolume>());
        }
    }

    public async Task<OkxHttpGetMarginLendingRatioResponse> GetMarginLendingRatio(string currency,
        string? begin = null, string? end = null, string period = "5m",
        CancellationToken cancellationToken = default)
    {
        var url = await _urlBuilder.UriCombine(_options.GetMarginLendingRatio, ccy: currency,
                begin: begin, end: end, period: period,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using (_client.UseLimiter<RubikMarginLendingRatioHttpOkxLimiter>("", "Http:GetMarginLendingRatio"))
        {
            return await _client
                       .GetFromJsonAsync<OkxHttpGetMarginLendingRatioResponse>(url, _jsonSerializerOptions.Value,
                           cancellationToken)
                       .ConfigureAwait(false) ??
                   new OkxHttpGetMarginLendingRatioResponse(-1, "", new List<OkxHttpRubikMarginLendingRatio>());
        }
    }

    public async Task<OkxHttpGetLongShortRatioResponse> GetLongShortRatio(string currency,
        string? begin = null, string? end = null, string period = "5m",
        CancellationToken cancellationToken = default)
    {
        var url = await _urlBuilder.UriCombine(_options.GetLongShortRatio, ccy: currency,
                begin: begin, end: end, period: period,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using (_client.UseLimiter<RubikLongShortRatioHttpOkxLimiter>("", "Http:GetLongShortRatio"))
        {
            return await _client
                       .GetFromJsonAsync<OkxHttpGetLongShortRatioResponse>(url, _jsonSerializerOptions.Value,
                           cancellationToken)
                       .ConfigureAwait(false) ??
                   new OkxHttpGetLongShortRatioResponse(-1, "", new List<OkxHttpRubikLongShortRatio>());
        }
    }

    public async Task<OkxHttpGetContractsOpenInterestAndVolumeVolumeResponse> GetContractsOpenInterestAndVolume(
        string currency,
        string? begin = null, string? end = null, string period = "5m",
        CancellationToken cancellationToken = default)
    {
        var url = await _urlBuilder.UriCombine(_options.GetContractsOpenInterestAndVolume, ccy: currency,
                begin: begin, end: end, period: period,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using (_client.UseLimiter<RubikContractsOpenInterestsAndVolumeHttpOkxLimiter>("",
                   "Http:GetContractsOpenInterestAndVolume"))
        {
            return await _client
                       .GetFromJsonAsync<OkxHttpGetContractsOpenInterestAndVolumeVolumeResponse>(url,
                           _jsonSerializerOptions.Value,
                           cancellationToken)
                       .ConfigureAwait(false) ??
                   new OkxHttpGetContractsOpenInterestAndVolumeVolumeResponse(-1, "",
                       new List<OkxHttpRubikOpenInterestVolume>());
        }
    }

    private static JsonSerializerOptions CreateJsonSerializerOptions()
    {
        var res = new JsonSerializerOptions
            { NumberHandling = JsonNumberHandling.AllowReadingFromString, PropertyNameCaseInsensitive = true };
        res.Converters.Add(new JsonDoubleConverter());
        res.Converters.Add(new JsonNullableDoubleConverter());
        res.Converters.Add(new SafeJsonDoubleConverter<SafeJsonDoubleDefaultValue>());
        res.Converters.Add(new SafeJsonDoubleConverter<SafeJsonDoubleDefaultValueNegativeZero>());
        res.Converters.Add(new SafeJsonDoubleConverter<SafeJsonDoubleDefaultValueOne>());
        res.Converters.Add(new JsonLongConverter());
        res.Converters.Add(new PooledStringJsonConverter(StringPool));
        res.Converters.Add(new PooledListConverter<OkxHttpTickerInfo>());
        res.Converters.Add(new PooledListConverter<OkxHttpOpenInterest>());
        var fundingRateJsonConverter = new OkxHttpFundingRateJsonConverter();
        res.Converters.Add(new OneItemListJsonConverter<OkxHttpFundingRate>
            { InnerConverter = fundingRateJsonConverter });
        res.Converters.Add(fundingRateJsonConverter);
        return res;
    }
}