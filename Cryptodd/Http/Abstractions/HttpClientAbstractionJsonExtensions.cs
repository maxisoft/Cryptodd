using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Cryptodd.Binance.Http;
using JasperFx.Core;
using Microsoft.Extensions.Options;

namespace Cryptodd.Http.Abstractions;

public static class HttpClientAbstractionJsonExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Task<TValue?> GetFromJsonAsync<TValue>(this IHttpClientAbstraction client, Uri? requestUri,
        JsonSerializerOptions? options, CancellationToken cancellationToken)
    {
        var taskResponse = client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return GetFromJsonAsyncCore<TValue>(taskResponse, options, cancellationToken);
    }

    private static async Task<T?> GetFromJsonAsyncCore<T>(Task<HttpResponseMessage> taskResponse,
        JsonSerializerOptions? options, CancellationToken cancellationToken)
    {
        using var response = await taskResponse.ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await ReadFromJsonAsyncHelper<T>(response.Content, options, cancellationToken).ConfigureAwait(false);
    }

    private static Task<T?> ReadFromJsonAsyncHelper<T>(HttpContent content, JsonSerializerOptions? options,
        CancellationToken cancellationToken)
        => content.ReadFromJsonAsync<T>(options, cancellationToken);
}