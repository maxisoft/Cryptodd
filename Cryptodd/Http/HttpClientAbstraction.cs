using System.Diagnostics.CodeAnalysis;

namespace Cryptodd.Http;

public abstract class HttpClientAbstraction : IHttpClientAbstraction
{
    public HttpClient Client { get; set; }


    protected HttpClientAbstraction(HttpClient client)
    {
        Client = client;
    }

    protected virtual void PreSendAsync(ref HttpRequestMessage request, ref HttpCompletionOption completionOption,
        ref CancellationToken cancellationToken) { }

    protected virtual ValueTask<HttpResponseMessage> PostSendAsync(HttpResponseMessage result,
        HttpRequestMessage request, HttpCompletionOption completionOption,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult<HttpResponseMessage>(result);

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        PreSendAsync(ref request, ref completionOption, ref cancellationToken);
        var res = await Client.SendAsync(request, completionOption, cancellationToken).ConfigureAwait(false);
        return await PostSendAsync(res, request, completionOption, cancellationToken).ConfigureAwait(false);
    }

    Version IHttpClientAbstraction.DefaultRequestVersion => Client.DefaultRequestVersion;
    HttpVersionPolicy IHttpClientAbstraction.DefaultVersionPolicy => Client.DefaultVersionPolicy;
}