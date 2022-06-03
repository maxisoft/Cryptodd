namespace Cryptodd.Http;

public class UriRewriteService : IUriRewriteService
{
    public ValueTask<Uri> Rewrite(Uri uri) => ValueTask.FromResult(uri);
}