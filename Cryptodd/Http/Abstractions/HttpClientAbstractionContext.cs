namespace Cryptodd.Http.Abstractions;

public record HttpClientAbstractionContext(Uri? Uri = null, Uri? OriginalUri = null);