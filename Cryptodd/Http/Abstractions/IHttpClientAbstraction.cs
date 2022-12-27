namespace Cryptodd.Http.Abstractions;

public interface IHttpClientAbstraction
{
    public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, HttpCompletionOption completionOption,
        CancellationToken cancellationToken);


    public Uri? CreateUri(string? uri);

    protected static Uri? DoCreateUri(string? uri) =>
        string.IsNullOrEmpty(uri) ? null : new Uri(uri, UriKind.RelativeOrAbsolute);


    public HttpRequestMessage CreateRequestMessage(HttpMethod method, Uri? uri);

    protected static HttpRequestMessage DoCreateRequestMessage<T>(in T client, HttpMethod method, Uri? uri)
        where T : IHttpClientAbstraction =>
        new(method, uri)
            { Version = client.DefaultRequestVersion, VersionPolicy = client.DefaultVersionPolicy };


    public Version DefaultRequestVersion { get; }

    public HttpVersionPolicy DefaultVersionPolicy { get; }

    public HttpCompletionOption DefaultCompletionOption => HttpCompletionOption.ResponseContentRead;
}