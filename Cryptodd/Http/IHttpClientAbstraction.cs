using System.Diagnostics.CodeAnalysis;

namespace Cryptodd.Http;

public interface IHttpClientAbstraction
{
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption,
        CancellationToken cancellationToken);

    public Task<HttpResponseMessage> GetAsync([StringSyntax(StringSyntaxAttribute.Uri)] string? requestUri) =>
        GetAsync(CreateUri(requestUri));

    public Task<HttpResponseMessage> GetAsync(Uri? requestUri) =>
        GetAsync(requestUri, DefaultCompletionOption);

    public Task<HttpResponseMessage> GetAsync([StringSyntax(StringSyntaxAttribute.Uri)] string? requestUri,
        HttpCompletionOption completionOption) =>
        GetAsync(CreateUri(requestUri), completionOption);

    public Task<HttpResponseMessage> GetAsync(Uri? requestUri, HttpCompletionOption completionOption) =>
        GetAsync(requestUri, completionOption, CancellationToken.None);

    public Task<HttpResponseMessage> GetAsync([StringSyntax(StringSyntaxAttribute.Uri)] string? requestUri,
        CancellationToken cancellationToken) =>
        GetAsync(CreateUri(requestUri), cancellationToken);

    public Task<HttpResponseMessage> GetAsync(Uri? requestUri, CancellationToken cancellationToken) =>
        GetAsync(requestUri, DefaultCompletionOption, cancellationToken);

    public Task<HttpResponseMessage> GetAsync([StringSyntax(StringSyntaxAttribute.Uri)] string? requestUri,
        HttpCompletionOption completionOption, CancellationToken cancellationToken) =>
        GetAsync(CreateUri(requestUri), completionOption, cancellationToken);

    public Task<HttpResponseMessage> GetAsync(Uri? requestUri, HttpCompletionOption completionOption,
        CancellationToken cancellationToken) =>
        SendAsync(CreateRequestMessage(HttpMethod.Get, requestUri), completionOption, cancellationToken);


    public Uri? CreateUri(string? uri) => DoCreateUri(uri);

    protected static Uri? DoCreateUri(string? uri) =>
        string.IsNullOrEmpty(uri) ? null : new Uri(uri, UriKind.RelativeOrAbsolute);


    public HttpRequestMessage CreateRequestMessage(HttpMethod method, Uri? uri) =>
        DoCreateRequestMessage(this, method, uri);

    protected static HttpRequestMessage DoCreateRequestMessage<T>(in T client, HttpMethod method, Uri? uri)
        where T : IHttpClientAbstraction =>
        new(method, uri)
            { Version = client.DefaultRequestVersion, VersionPolicy = client.DefaultVersionPolicy };


    public Version DefaultRequestVersion { get; }

    public HttpVersionPolicy DefaultVersionPolicy { get; }

    public const HttpCompletionOption DefaultCompletionOption = HttpCompletionOption.ResponseContentRead;
}