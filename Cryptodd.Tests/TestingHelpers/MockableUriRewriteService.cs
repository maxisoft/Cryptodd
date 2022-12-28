using System;
using System.Threading.Tasks;
using Cryptodd.Http;

namespace Cryptodd.Tests.TestingHelpers;

public class MockableUriRewriteService : IUriRewriteService
{
    public IUriRewriteService RewriteService { get; set; } = new UriRewriteService();
    public virtual async ValueTask<Uri> Rewrite(Uri uri) => await RewriteService.Rewrite(uri);
}