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

public class GatherGroupedOrderbookServiceTest : IDisposable
{
    private readonly string _tmpPath;
    private readonly Container _container;
    private readonly Mock<StaticPairFilterLoader> _pairFilterLoaderMock;
    private readonly Mock<GroupedOrderBookHandler> _groupedObHandlerMock;

    public GatherGroupedOrderbookServiceTest()
    {
        _tmpPath = Path.GetTempPath();
        _tmpPath = Path.Combine(_tmpPath, new Guid().ToString());
        Directory.CreateDirectory(_tmpPath);
        _pairFilterLoaderMock = new Mock<StaticPairFilterLoader> { CallBase = true };
        _groupedObHandlerMock = new Mock<GroupedOrderBookHandler> { CallBase = true };
        _container = new ContainerFactory().CreateContainer(new CreateContainerOptions()
        {
            ScanForPlugins = false,
            ConfigurationServiceOptions = new ConfigurationServiceOptions()
            {
                WorkingDirectory = _tmpPath,
                DefaultBasePath = _tmpPath,
                ScanForEnvConfig = false,
                DefaultConfig = new OrderedDictionary<string, string>()
            },

            PostConfigure = c =>
            {
                // RemoveAll doesnt work in lamar ?
                // https://jasperfx.github.io/lamar/documentation/ioc/registration/changing-configuration-at-runtime/
                c.RemoveAll<IGroupedOrderbookHandler>();
                c.RemoveAll<IPairFilterLoader>();
                c.ForSingletonOf<IPairFilterLoader>().Use(_pairFilterLoaderMock.Object);
                c.ForSingletonOf<Mock<GroupedOrderBookHandler>>().Use(_groupedObHandlerMock);
                c.ForSingletonOf<IGroupedOrderbookHandler>().Use(_groupedObHandlerMock.Object);
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

        public virtual Task Handle(IReadOnlyCollection<GroupedOrderbookDetails> orderbooks,
            CancellationToken cancellationToken)
        {
            ReplayMemory.Add(orderbooks.ToArray());
            return Task.CompletedTask;
        }

        public bool Disabled { get; set; }
    }

    public class StaticPairFilterLoader : IPairFilterLoader
    {
        internal readonly Mock<PairFilter> PairFilter;

        public StaticPairFilterLoader()
        {
            PairFilter = new Mock<PairFilter>() { CallBase = true };
        }

        public void AddAll(string input)
        {
            PairFilter.Object.AddAll(input);
        }

        public virtual ValueTask<IPairFilter> GetPairFilterAsync(string name,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IPairFilter>(PairFilter.Object);
        }
    }

    public void Dispose()
    {
        Directory.Delete(_tmpPath, true);
        _container.Dispose();
    }

    [Fact]
    public async void Test_NominalCase_Using_Remote()
    {
        var pairs = new string[] { "BTC-PERP", "ETH/USDT" };
        _pairFilterLoaderMock.Object.AddAll(string.Join(";", pairs));
        
        using var service = _container.GetInstance<GatherGroupedOrderBookService>();
        using var cts = new CancellationTokenSource(60 * 1000);
        await service.CollectOrderBooks(cts.Token);

        var replayMemory = _groupedObHandlerMock.Object.ReplayMemory;
        
        Assert.NotEmpty(pairs);
        Assert.Single(replayMemory);
        Assert.NotEmpty(replayMemory[0]);
        foreach (var pair in pairs)
        {
            Assert.NotEmpty(replayMemory[0].Where(details => details.Market == pair));
        }

        Assert.Equal(pairs.Length, replayMemory[0].Count);

        _pairFilterLoaderMock.Verify(
            loader => loader.GetPairFilterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _pairFilterLoaderMock.VerifyNoOtherCalls();

        _groupedObHandlerMock.Verify(
            handler => handler.Handle(It.IsAny<IReadOnlyCollection<GroupedOrderbookDetails>>(),
                It.IsAny<CancellationToken>()), Times.Once);
        _groupedObHandlerMock.VerifyNoOtherCalls();
    }
}