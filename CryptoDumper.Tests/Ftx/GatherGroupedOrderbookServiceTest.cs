using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CryptoDumper.Ftx;
using CryptoDumper.Ftx.Models;
using CryptoDumper.IoC;
using CryptoDumper.IoC.Registries.Customs;
using CryptoDumper.Pairs;
using Lamar;
using Maxisoft.Utils.Collections.Dictionaries;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Xunit;

namespace CryptoDumper.Tests.Ftx;

public class GatherGroupedOrderbookServiceTest: IDisposable
{
    private readonly string tmpPath;
    private readonly Container _container;
    private readonly Mock<StaticPairFilterLoader> PairFilterLoaderMock;
    private readonly Mock<GroupedOrderBookHandler> GroupedObHandlerMock;

    public GatherGroupedOrderbookServiceTest()
    {
        tmpPath = Path.GetTempPath();
        tmpPath = Path.Combine(tmpPath, new Guid().ToString());
        Directory.CreateDirectory(tmpPath);
        PairFilterLoaderMock = new Mock<StaticPairFilterLoader> {CallBase = true};
        GroupedObHandlerMock = new Mock<GroupedOrderBookHandler> {CallBase = true};
        _container = new ContainerFactory().CreateContainer(new CreateContainerOptions()
        {
            ScanForPlugins = false,
            ConfigurationServiceOptions = new ConfigurationServiceOptions()
            {
                WorkingDirectory = tmpPath,
                DefaultBasePath = tmpPath,
                ScanForEnvConfig = false,
                DefaultConfig = new OrderedDictionary<string, string>()
            },
            
            PostConfigure = c => {
                // RemoveAll doesnt work in lamar ?
                // https://jasperfx.github.io/lamar/documentation/ioc/registration/changing-configuration-at-runtime/
                c.RemoveAll<IGroupedOrderbookHandler>();
                c.RemoveAll<IPairFilterLoader>();
                c.ForSingletonOf<IPairFilterLoader>().Use(PairFilterLoaderMock.Object);
                c.ForSingletonOf<Mock<GroupedOrderBookHandler>>().Use(GroupedObHandlerMock);
                c.ForSingletonOf<IGroupedOrderbookHandler>().Use(GroupedObHandlerMock.Object);
            }
        });
        
        foreach (var handler in _container.GetAllInstances<IGroupedOrderbookHandler>())
        {
            handler.Disabled = true;
        }

        _container.GetInstance<Mock<GroupedOrderBookHandler>>().Object.Disabled = false;
    }

    public class GroupedOrderBookHandler : IGroupedOrderbookHandler
    {
        internal List<IReadOnlyCollection<GroupedOrderbookDetails>> ReplayMemory =
            new List<IReadOnlyCollection<GroupedOrderbookDetails>>();

        public Task Handle(IReadOnlyCollection<GroupedOrderbookDetails> orderbooks, CancellationToken cancellationToken)
        {
            ReplayMemory.Add(orderbooks.ToArray());
            return Task.CompletedTask;
        }

        public bool Disabled { get; set; }
    }
    
    public class StaticPairFilterLoader : IPairFilterLoader
    {
        internal static readonly Mock<PairFilter> PairFilter;

        static StaticPairFilterLoader()
        {
            PairFilter = new Mock<PairFilter>() {CallBase = true};
            PairFilter.Object.AddAll("BTC-PERP; ETH/USDT");
        }

        public virtual ValueTask<IPairFilter> GetPairFilterAsync(string name, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IPairFilter>(PairFilter.Object);
        }
    }

    public void Dispose()
    {
        Directory.Delete(tmpPath, true);
        _container.Dispose();
    }

    [Fact]
    public async void Test_NominalCase_Using_Remote()
    {
        using var service = _container.GetInstance<GatherGroupedOrderBookService>();
        using var cts = new CancellationTokenSource(60 * 1000);
        await service.CollectOrderBooks(cts.Token);

        var replayMemory = GroupedObHandlerMock.Object.ReplayMemory;
        
        Assert.NotEmpty(replayMemory);
        Assert.NotEmpty(replayMemory[0]);
        Assert.NotEmpty(replayMemory[0].Where(details => details.Market == "ETH/USDT"));
        Assert.NotEmpty(replayMemory[0].Where(details => details.Market == "BTC-PERP"));
        
        PairFilterLoaderMock.Verify(loader => loader.GetPairFilterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        PairFilterLoaderMock.VerifyNoOtherCalls();
        
        
    }
    
}