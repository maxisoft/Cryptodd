using System.Runtime.CompilerServices;
using Cryptodd.Okx.Models;

namespace Cryptodd.Okx.Http;

public sealed class OkxHttpUrlBuilder
{
    private readonly OkxPublicHttpApiOptions _options;

    internal OkxHttpUrlBuilder(OkxPublicHttpApiOptions options)
    {
        _options = options;
    }

    public string BaseUrl => _options.BaseUrl;

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public ValueTask<Uri> UriCombine(string url, string? instrumentType = null, string? underlying = null,
        string? instrumentFamily = null, string? instrumentId = null,
        string? expiryTime = null, CancellationToken cancellationToken = default)
    {
        UriBuilder builder;
        if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out var res) &&
            res is { IsAbsoluteUri: true, IsFile: false, Scheme: "https" or "http" })
        {
            builder = new UriBuilder(res)
                .WithPathSegment(url);
        }
        else
        {
            builder = new UriBuilder(BaseUrl)
                .WithPathSegment(url);
        }

        if (instrumentType is not null)
        {
            builder = builder.WithParameter("instType", instrumentType);
        }

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

        if (expiryTime is not null)
        {
            builder = builder.WithParameter("expTime", expiryTime);
        }

        return ValueTask.FromResult(builder.Uri);
    }

    public ValueTask<Uri> UriCombine(string url, OkxInstrumentType instrumentType, string? underlying = null,
        string? instrumentFamily = null, string? instrumentId = null,
        string? expiryTime = null, CancellationToken cancellationToken = default)
        => UriCombine(url, instrumentType.ToHttpString(), underlying,
            instrumentFamily, instrumentId, expiryTime, cancellationToken);
}