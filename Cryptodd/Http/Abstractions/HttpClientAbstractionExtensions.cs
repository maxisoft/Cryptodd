using System.Diagnostics.CodeAnalysis;

namespace Cryptodd.Http.Abstractions;

public static class HttpClientAbstractionExtensions
{
    public static Task<HttpResponseMessage> GetAsync<T>(this T client, [StringSyntax(StringSyntaxAttribute.Uri)] string? requestUri) where T: IHttpClientAbstraction=>
        client.GetAsync(client.CreateUri(requestUri));

    public static Task<HttpResponseMessage> GetAsync<T>(this T client, Uri? requestUri) where T: IHttpClientAbstraction=>
        client.GetAsync(requestUri, client.DefaultCompletionOption);

    public static Task<HttpResponseMessage> GetAsync<T>(this T client, [StringSyntax(StringSyntaxAttribute.Uri)] string? requestUri,
        HttpCompletionOption completionOption) where T: IHttpClientAbstraction=>
        client.GetAsync(client.CreateUri(requestUri), completionOption);

    public static Task<HttpResponseMessage> GetAsync<T>(this T client, Uri? requestUri, HttpCompletionOption completionOption) where T: IHttpClientAbstraction=>
        client.GetAsync(requestUri, completionOption, CancellationToken.None);

    public static Task<HttpResponseMessage> GetAsync<T>(this T client, [StringSyntax(StringSyntaxAttribute.Uri)] string? requestUri,
        CancellationToken cancellationToken) where T: IHttpClientAbstraction=>
        client.GetAsync(client.CreateUri(requestUri), cancellationToken);

    public static Task<HttpResponseMessage> GetAsync<T>(this T client, Uri? requestUri, CancellationToken cancellationToken)where T: IHttpClientAbstraction =>
        client.GetAsync(requestUri, client.DefaultCompletionOption, cancellationToken);

    public static Task<HttpResponseMessage> GetAsync<T>(this T client, [StringSyntax(StringSyntaxAttribute.Uri)] string? requestUri,
        HttpCompletionOption completionOption, CancellationToken cancellationToken)where T: IHttpClientAbstraction =>
        client.GetAsync(client.CreateUri(requestUri), completionOption, cancellationToken);

    public static Task<HttpResponseMessage> GetAsync<T>(this T client, Uri? requestUri, HttpCompletionOption completionOption,
        CancellationToken cancellationToken) where T: IHttpClientAbstraction=>
        client.SendAsync(client.CreateRequestMessage(HttpMethod.Get, requestUri), completionOption, cancellationToken);
}