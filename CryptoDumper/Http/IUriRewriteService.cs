using CryptoDumper.IoC;

namespace CryptoDumper.Http;

public interface IUriRewriteService : IService
{
    ValueTask<Uri> Rewrite(Uri uri);
}