using System.Text.Json;
using System.Text.Json.Nodes;
using Cryptodd.Http;
using Cryptodd.Http.Abstractions;
using Cryptodd.IoC;
using Microsoft.Extensions.Configuration;

namespace Cryptodd.Okx.Http;

[Flags]
public enum OkxInstrumentType
{
    Spot = 1,
    Margin = 1 << 1,
    Swap = 1 << 2,
    Futures = 1 << 3,
    Option = 1 << 4
}

public static class OkxInstrumentTypeExtensions
{
    public static string ToHttpString(this OkxInstrumentType instrumentType) =>
        Enum.GetName(instrumentType)?.ToUpperInvariant() ??
        throw new ArgumentOutOfRangeException(nameof(instrumentType));
}

public class OkxPublicHttpApiOptions
{
    public const string DefaultBaseUrl = "https://aws.okx.com";
    public string BaseUrl { get; set; } = DefaultBaseUrl;

    public string GetInstrumentsUrl { get; set; } = "/api/v5/public/instruments";
}

public class OkxPublicHttpApi: IService
{
    private readonly IOkxHttpClientAbstraction _client;
    private readonly OkxPublicHttpApiOptions _options = new();


    public OkxPublicHttpApi(IOkxHttpClientAbstraction client, IConfiguration configuration)
    {
        _client = client;
        configuration.GetSection("Okx:Http").Bind(_options);
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
}