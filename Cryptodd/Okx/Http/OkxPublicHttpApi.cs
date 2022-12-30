using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Cryptodd.Binance.Json;
using Cryptodd.Http;
using Cryptodd.Http.Abstractions;
using Cryptodd.IoC;
using Cryptodd.Json;
using Cryptodd.Json.Converters;
using Cryptodd.Okx.Http.Abstractions;
using Cryptodd.Okx.Models;
using Maxisoft.Utils.Collections.Lists.Specialized;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.Okx.Http;

public class OkxPublicHttpApi : IOkxInstrumentIdsProvider, IService
{
    private readonly IOkxHttpClientAbstraction _client;
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
        var resp = await GetInstruments(instrumentType, underlying, instrumentFamily, instrumentId, cancellationToken)
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

    public async Task<JsonObject> GetInstruments(OkxInstrumentType instrumentType, string? underlying = null,
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


    public async Task<GetTikersResponse> GetTickers(OkxInstrumentType instrumentType, string? underlying = null,
        string? instrumentFamily = null, CancellationToken cancellationToken = default)
    {
        var instrumentTypeString = instrumentType.ToHttpString();
        var url = await _urlBuilder.UriCombine(_options.GetTickersUrl, instrumentType: instrumentTypeString,
                underlying: underlying, instrumentFamily: instrumentFamily, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        using (_client.UseLimiter<InstrumentsHttpOkxLimiter>("", "Http:GetTickers"))
        {
            return await _client.GetFromJsonAsync<GetTikersResponse>(url, _jsonSerializerOptions.Value, cancellationToken)
                .ConfigureAwait(false) ?? new GetTikersResponse(-1, "", new PooledList<TickerInfo>());
        }
    }

    private static JsonSerializerOptions CreateJsonSerializerOptions()
    {
        var res = new JsonSerializerOptions
            { NumberHandling = JsonNumberHandling.AllowReadingFromString, PropertyNameCaseInsensitive = true };
        res.Converters.Add(new JsonDoubleConverter());
        res.Converters.Add(new JsonNullableDoubleConverter());
        res.Converters.Add(new SafeJsonDoubleConverter<SafeJsonDoubleDefaultValueNegativeZero>());
        res.Converters.Add(new JsonLongConverter());
        res.Converters.Add(new PooledStringJsonConverter(StringPool));
        res.Converters.Add(new PooledListConverter<TickerInfo>());
        return res;
    }

    private Lazy<JsonSerializerOptions> _jsonSerializerOptions;

    private static readonly StringPool StringPool = new(10 << 10);
}