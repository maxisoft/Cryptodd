using Cryptodd.IoC;

namespace Cryptodd.Http;

public interface IUriRewriteService : IService
{
    ValueTask<Uri> Rewrite(Uri uri);
}