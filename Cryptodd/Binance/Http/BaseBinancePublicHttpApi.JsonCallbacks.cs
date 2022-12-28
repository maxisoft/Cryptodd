using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Cryptodd.Http;
using Cryptodd.Http.Abstractions;
using JasperFx.Core;
using Maxisoft.Utils.Collections.LinkedLists;

namespace Cryptodd.Binance.Http;

public class ChangedToBinanceUsaHttpRequestException : HttpRequestException
{
    public ChangedToBinanceUsaHttpRequestException(string? message, Exception? inner, HttpStatusCode? statusCode) :
        base(message, inner, statusCode)
    {
        
    }
}

public abstract partial class BaseBinancePublicHttpApi<TOptions, TInternalBinanceRateLimiter, THttpClientAbstraction>
{
    private AsyncLocal<LinkedListAsIList<Action<HttpResponseMessage>>> HttpMessageCallbacks { get; } = new();

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

            usedWeightFloat *= Options.UsedWeightMultiplier;
            var now = DateTimeOffset.Now;
            var date = headers.Date ?? now;
            if ((date - now).Duration() > TimeSpan.FromMinutes(1))
            {
                date = now;
            }

            InternalRateLimiter.UpdateUsedWeightFromBinance(checked((int)usedWeightFloat), date);
        }

        if (response.StatusCode is (HttpStatusCode)418 or (HttpStatusCode)429)
        {
            Logger.Warning("Got Status {Status} from binance, going to downscale {AvailableWeightMultiplier}", response.StatusCode, nameof(InternalRateLimiter.AvailableWeightMultiplier));
            InternalRateLimiter.UpdateUsedWeightFromBinance(
                (int)((ulong)InternalRateLimiter.MaxUsableWeight + 1 > int.MaxValue
                    ? int.MaxValue
                    : (ulong)InternalRateLimiter.MaxUsableWeight + 1));
            
            InternalRateLimiter.AvailableWeightMultiplier *= 0.9f;
        }

        if (response.StatusCode is (HttpStatusCode)451 && Options.ChangeAddressToUSA())
        {
            Logger.Warning("Changing default address to binance usa");
            throw new ChangedToBinanceUsaHttpRequestException(null, null, response.StatusCode);
        }
    }

    protected RemoveCallbackOnDispose<HttpResponseMessage> AddResponseCallbacks(
        Action<HttpResponseMessage> action)
    {
        HttpMessageCallbacks.Value ??= new LinkedListAsIList<Action<HttpResponseMessage>>();
        var node = HttpMessageCallbacks.Value.AddLast(action);
        return new RemoveCallbackOnDispose<HttpResponseMessage>(node);
    }

    protected sealed class RemoveCallbackOnDispose<T> : IDisposable
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

            lock (_node)
            {
                _node?.List?.Remove(_node);
                _node = null;
            }
        }
    }

    #region HttpClientJsonExtensions copy pasted code + adapted

    private Task<TValue?> DoGetFromJsonAsync<TValue>(THttpClientAbstraction client, Uri? requestUri,
        JsonSerializerOptions? options, CancellationToken cancellationToken)
    {
        var taskResponse = client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        return GetFromJsonAsyncCore<TValue>(taskResponse, options, cancellationToken);
    }

    private Uri? ReplaceHostToUsa(Uri? uri)
    {
        if (uri is null)
        {
            return null;
        }

        var s = uri.ToString();
        return new Uri(s.ReplaceFirst(uri.Host, new Uri(Options.BaseAddress).Host));
    }
    
    protected Task<TValue?> GetFromJsonAsync<TValue>(THttpClientAbstraction client, Uri? requestUri,
        JsonSerializerOptions? options, CancellationToken cancellationToken)
    {
        try
        {
            return DoGetFromJsonAsync<TValue>(client, requestUri, options, cancellationToken);
        }
        catch (ChangedToBinanceUsaHttpRequestException)
        {
            var newUri = ReplaceHostToUsa(requestUri);
            Debug.Assert(newUri != requestUri, "newUri != requestUri");
            return DoGetFromJsonAsync<TValue>(client, newUri, options, cancellationToken);
        }
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
}