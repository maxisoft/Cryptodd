using System.Text.Json;
using System.Text.Json.Nodes;
using Cryptodd.Http;
using Cryptodd.Http.Abstractions;
using Cryptodd.IoC;
using Cryptodd.Okx.Http.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.Okx.Http;

public class OkxPublicHttpApi : IOkxInstrumentIdsProvider, IService
{
    private readonly IOkxHttpClientAbstraction _client;
    private readonly OkxPublicHttpApiOptions _options = new();


    public OkxPublicHttpApi(IOkxHttpClientAbstraction client, IConfiguration configuration)
    {
        _client = client;
        configuration.GetSection("Okx:Http").Bind(_options);
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

    protected ValueTask<Uri> UriCombine(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var res))
        {
            return ValueTask.FromResult(res);
        }

        var uri =
            new UriBuilder(_options.BaseUrl)
                .WithPathSegment(url)
                .Uri;
        return ValueTask.FromResult(uri);
    }

    public async Task<JsonObject> GetInstruments(OkxInstrumentType instrumentType, string? underlying = null,
        string? instrumentFamily = null, string? instrumentId = null, CancellationToken cancellationToken = default)
    {
        var instrumentTypeString = instrumentType.ToHttpString();
        var url = await UriCombine(_options.GetInstrumentsUrl).ConfigureAwait(false);
        var builder = new UriBuilder(url).WithParameter("instType", instrumentTypeString);
        if (underlying is not null)
        {
            builder = builder.WithParameter("uly", underlying);
        }

        if (instrumentFamily is not null)
        {
            builder = builder.WithParameter("instFamily", instrumentFamily);
        }

        if (instrumentId is not null)
        {
            builder = builder.WithParameter("instId", instrumentId);
        }

        url = builder.Uri;
        using (_client.UseLimiter<InstrumentsHttpOkxLimiter>(instrumentTypeString, "Http:ListInstruments"))
        {
            return await _client.GetFromJsonAsync<JsonObject>(url, JsonSerializerOptions.Default, cancellationToken)
                .ConfigureAwait(false) ?? new JsonObject();
        }
    }
}