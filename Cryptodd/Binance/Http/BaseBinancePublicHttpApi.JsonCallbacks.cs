using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Maxisoft.Utils.Collections.LinkedLists;

namespace Cryptodd.Binance.Http;

public abstract partial class BaseBinancePublicHttpApi<TOptions, TInternalBinanceRateLimiter>
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
            InternalRateLimiter.UpdateUsedWeightFromBinance(
                (int)((ulong)InternalRateLimiter.MaxUsableWeight + 1 > int.MaxValue
                    ? int.MaxValue
                    : (ulong)InternalRateLimiter.MaxUsableWeight + 1));
            InternalRateLimiter.AvailableWeightMultiplier *= 0.9f;
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

            lock (this)
            {
                _node?.List?.Remove(_node);
                _node = null;
            }
        }
    }

    #region HttpClientJsonExtensions copy pasted code + adapted

    protected Task<TValue?> GetFromJsonAsync<TValue>(HttpClient client, Uri? requestUri,
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
}