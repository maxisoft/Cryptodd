﻿namespace Cryptodd.Http.Abstractions;

public abstract class HttpClientAbstraction : IHttpClientAbstraction
{
    public HttpClient Client { get; set; }


    protected HttpClientAbstraction(HttpClient client)
    {
        Client = client;
    }

    protected virtual ValueTask<(HttpRequestMessage, HttpCompletionOption, CancellationToken)> PreSendAsync(
        HttpRequestMessage request, HttpCompletionOption completionOption,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<(HttpRequestMessage, HttpCompletionOption, CancellationToken)>((request,
            completionOption, cancellationToken));

    protected virtual ValueTask<HttpResponseMessage> PostSendAsync(HttpResponseMessage result,
        HttpRequestMessage request, HttpCompletionOption completionOption,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<HttpResponseMessage>(result);
    
    protected virtual ValueTask<(Exception?, HttpResponseMessage?)> ErrorSendAsync(Exception exception, HttpResponseMessage? result,
        HttpRequestMessage request, HttpCompletionOption completionOption,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<(Exception?, HttpResponseMessage?)>((exception, result));

    public virtual async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        (request, completionOption, cancellationToken) =
            await PreSendAsync(request, completionOption, cancellationToken);
        HttpResponseMessage res;
        try
        {
            res = await Client.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            var (e2, message) = await ErrorSendAsync(e, null, request, completionOption, cancellationToken).ConfigureAwait(false);
            if (e2 is not null)
            {
                throw e2;
            }

            if (message is not null)
            {
                res = message;
            }
            else
            {
                throw;
            }
        }
        
        return await PostSendAsync(res, request, completionOption, cancellationToken).ConfigureAwait(false);
    }

    public virtual Uri? CreateUri(string? uri) => IHttpClientAbstraction.DoCreateUri(uri);

    Version IHttpClientAbstraction.DefaultRequestVersion => Client.DefaultRequestVersion;
    HttpVersionPolicy IHttpClientAbstraction.DefaultVersionPolicy => Client.DefaultVersionPolicy;

    public virtual HttpRequestMessage CreateRequestMessage(HttpMethod method, Uri? uri) =>
        IHttpClientAbstraction.DoCreateRequestMessage(this, method, uri);
}