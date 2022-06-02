using Cryptodd.IoC;

namespace Cryptodd.Http;

public class UriRewriteService : IUriRewriteService
{
    public ValueTask<Uri> Rewrite(Uri uri)
    {
        return ValueTask.FromResult<Uri>(uri);
    }
}