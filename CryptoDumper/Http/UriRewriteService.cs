using CryptoDumper.IoC;

namespace CryptoDumper.Http;

public class UriRewriteService : IUriRewriteService
{
    public ValueTask<Uri> Rewrite(Uri uri)
    {
        return ValueTask.FromResult<Uri>(uri);
    }
}