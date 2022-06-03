using System.Text;
using Cryptodd.Ftx;
using Cryptodd.IoC;
using Cryptodd.Plugins;
using Lamar;

namespace Cryptodd.Console;

internal class Program
{
    private static async Task Main(string[] args)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var rootContainer = new ContainerFactory().CreateContainer();

        using var container = rootContainer.GetNestedContainer();
        container.Inject((IContainer)container);
        /*foreach (var typeRegistrer in container.GetAllInstances<ITypeRegistrer>())
        {
            typeRegistrer.RegisterType(BsonMapper.Global);
        }*/

        foreach (var plugin in container.GetAllInstances<IBasePlugin>().OrderBy(plugin => plugin.Order))
        {
            await plugin.OnStart();
        }

        var client = container.GetInstance<IFtxPublicHttpApi>();
        var resp = await client.GetAllFuturesAsync();
        var resp2 = await client.GetAllFundingRatesAsync();
        var ftxWs = container.GetInstance<GatherGroupedOrderBookService>();
        await ftxWs.CollectOrderBooks(default);
    }
}