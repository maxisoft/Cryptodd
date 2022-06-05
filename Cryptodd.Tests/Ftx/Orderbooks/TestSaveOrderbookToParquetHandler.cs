using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cryptodd.Ftx.Models;
using Cryptodd.Ftx.Orderbooks;
using Cryptodd.Ftx.Orderbooks.RegroupedOrderbooks;
using Cryptodd.IoC;
using Cryptodd.IoC.Registries.Customs;
using Cryptodd.Pairs;
using Cryptodd.Tests.Ftx.Orderbooks.RegroupedOrderbook;
using Lamar;
using Maxisoft.Utils.Collections.Dictionaries;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Parquet;
using Parquet.Data;
using Xunit;

namespace Cryptodd.Tests.Ftx.Orderbooks;

public class TestSaveOrderbookToParquetHandler
{
    private readonly string _tmpPath;
    private readonly Container _container;
    private readonly Mock<GatherGroupedOrderbookServiceTest.StaticPairFilterLoader> _pairFilterLoaderMock;
    private readonly Mock<GatherGroupedOrderbookServiceTest.GroupedOrderBookHandler> _groupedObHandlerMock;

    private readonly OrderedDictionary<string, string> DefaultConfig = new()
        { { "Ftx:GroupedOrderBook:Parquet:Enabled", "true" } };

    public TestSaveOrderbookToParquetHandler()
    {
        _tmpPath = Path.GetTempPath();
        _tmpPath = Path.Combine(_tmpPath, Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tmpPath);
        _pairFilterLoaderMock = new Mock<GatherGroupedOrderbookServiceTest.StaticPairFilterLoader> { CallBase = true };
        _groupedObHandlerMock = new Mock<GatherGroupedOrderbookServiceTest.GroupedOrderBookHandler> { CallBase = true };
        _container = new ContainerFactory().CreateContainer(new CreateContainerOptions()
        {
            ScanForPlugins = false,
            ConfigurationServiceOptions = new ConfigurationServiceOptions()
            {
                WorkingDirectory = _tmpPath,
                DefaultBasePath = _tmpPath,
                ScanForEnvConfig = false,
                DefaultConfig = DefaultConfig
            },

            PostConfigure = c =>
            {
                // RemoveAll doesnt work in lamar ?
                // https://jasperfx.github.io/lamar/documentation/ioc/registration/changing-configuration-at-runtime/
                c.RemoveAll<IGroupedOrderbookHandler>();
                c.RemoveAll<IPairFilterLoader>();
                c.ForSingletonOf<IPairFilterLoader>().Use(_pairFilterLoaderMock.Object);
                c.ForSingletonOf<Mock<GatherGroupedOrderbookServiceTest.GroupedOrderBookHandler>>()
                    .Use(_groupedObHandlerMock);
                c.ForSingletonOf<IGroupedOrderbookHandler>().Use(_groupedObHandlerMock.Object);
            }
        });

        foreach (var handler in _container.GetAllInstances<IGroupedOrderbookHandler>())
        {
            handler.Disabled = true;
        }

        foreach (var handler in _container.GetAllInstances<IRegroupedOrderbookHandler>())
        {
            handler.Disabled = true;
        }

        _container.GetInstance<Mock<GatherGroupedOrderbookServiceTest.GroupedOrderBookHandler>>().Object.Disabled =
            false;
    }

    [Fact]
    public async Task TestItCanWrite()
    {
        var content = await TestCreateGroupedOrderbook.GetFileContents("grouped_orderbook_btcusd.json");
        using var orderbookGroupedWrapper = JsonSerializer.Deserialize<GroupedOrderbookDetails>(content,
            FtxGroupedOrderBookWebsocket.OrderBookJsonSerializerOptions);
        Assert.NotNull(orderbookGroupedWrapper);

        var pairs = new string[] { "BTC.*", "ETH.*" };
        _pairFilterLoaderMock.Object.AddAll(string.Join(";", pairs));
        
        using var cts = new CancellationTokenSource(60 * 1000);
        IGroupedOrderbookHandler handler = _container.GetInstance<SaveOrderbookToParquetHandler>();
        await handler.Handle(new[] { orderbookGroupedWrapper! }, cts.Token);
        Assert.True(File.Exists(Path.Combine(_tmpPath, SaveOrderbookToParquetHandler.DefaultFileName)));
    }

    [Fact]
    public async Task TestItCanWrite2Items()
    {
        var content = await TestCreateGroupedOrderbook.GetFileContents("grouped_orderbook_btcusd.json");
        using var orderbookGroupedWrapper1 = JsonSerializer.Deserialize<GroupedOrderbookDetails>(content,
            FtxGroupedOrderBookWebsocket.OrderBookJsonSerializerOptions);
        Assert.NotNull(orderbookGroupedWrapper1);

        content = await TestCreateGroupedOrderbook.GetFileContents("grouped_orderbook_less_than_25.json");
        using var orderbookGroupedWrapper2 = JsonSerializer.Deserialize<GroupedOrderbookDetails>(content,
            FtxGroupedOrderBookWebsocket.OrderBookJsonSerializerOptions);
        Assert.NotNull(orderbookGroupedWrapper2);

        var pairs = new string[] { ".*" };
        _pairFilterLoaderMock.Object.AddAll(string.Join(";", pairs));

        var service = _container.GetInstance<GatherGroupedOrderBookService>();
        using var cts = new CancellationTokenSource(60 * 1000);
        IGroupedOrderbookHandler handler = _container.GetInstance<SaveOrderbookToParquetHandler>();
        await handler.Handle(new[] { orderbookGroupedWrapper1!, orderbookGroupedWrapper2! }, cts.Token);
        Assert.True(File.Exists(Path.Combine(_tmpPath, SaveOrderbookToParquetHandler.DefaultFileName)));
    }

    [Fact]
    public async Task TestReadItBack()
    {
        await TestItCanWrite2Items();

        var content = await TestCreateGroupedOrderbook.GetFileContents("grouped_orderbook_btcusd.json");
        using var orderbookGroupedWrapper = JsonSerializer.Deserialize<GroupedOrderbookDetails>(content,
            FtxGroupedOrderBookWebsocket.OrderBookJsonSerializerOptions);

        using (Stream fileStream =
               System.IO.File.OpenRead(Path.Combine(_tmpPath, SaveOrderbookToParquetHandler.DefaultFileName)))
        {
            using (var parquetReader = new ParquetReader(fileStream))
            {
                DataField[] dataFields = parquetReader.Schema.GetDataFields();
                Assert.NotEmpty(dataFields);
                Assert.Equal(new string[] { "time", "market", "grouping", "bid", "ask" },
                    dataFields.Select(field => field.Name));

                Assert.Equal(1, parquetReader.RowGroupCount);
                for (var i = 0; i < parquetReader.RowGroupCount; i++)
                {
                    using var groupReader = parquetReader.OpenRowGroupReader(i);
                    DataColumn[] columns = dataFields.Select(groupReader.ReadColumn).ToArray();
                    Assert.Equal(new dynamic[] { "BCH/JPY", "BTC/USD" },
                        columns[1].Data); // it must conserve order too
                    Assert.Equal(orderbookGroupedWrapper!.Grouping, ((double[])columns[2].Data)[^1]);
                    Assert.True(columns[4].HasRepetitions);
                    Assert.Equal(
                        orderbookGroupedWrapper!.Data.Asks.SelectMany(pair => new double?[]
                            { pair.Price, pair.Size }),
                        ((double?[])columns[4].Data)[^(orderbookGroupedWrapper!.Data.Asks.Count * 2)..]);
                }
            }
        }
    }

    public void Dispose()
    {
        Directory.Delete(_tmpPath, true);
        _container.Dispose();
    }
}