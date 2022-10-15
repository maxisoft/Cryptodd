using System;
using System.Collections;
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
using Maxisoft.Utils.Collections.Lists;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Parquet;
using Parquet.Data;
using Xunit;

namespace Cryptodd.Tests.Ftx.Orderbooks;

public class TestSaveRegroupedOrderbookToParquetHandler
{
    private readonly string _tmpPath;
    private readonly Container _container;
    private readonly Mock<GatherGroupedOrderbookServiceTest.StaticPairFilterLoader> _pairFilterLoaderMock;
    private readonly Mock<GatherGroupedOrderbookServiceTest.GroupedOrderBookHandler> _groupedObHandlerMock;

    private readonly OrderedDictionary<string, string> DefaultConfig = new()
        { { "Ftx:RegroupedOrderBook:Parquet:Enabled", "true" } };

    public TestSaveRegroupedOrderbookToParquetHandler()
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
        var regroupedOrderbook = RegroupedOrderbookAlgorithm.Create(orderbookGroupedWrapper!);
        var service = _container.GetInstance<GatherGroupedOrderBookService>();
        using var cts = new CancellationTokenSource(60 * 1000);
        IRegroupedOrderbookHandler handler = _container.GetInstance<SaveRegroupedOrderbookToParquetHandler>();
        await handler.Handle(new[] { regroupedOrderbook! }, cts.Token);
        Assert.True(File.Exists(Path.Combine(_tmpPath, SaveRegroupedOrderbookToParquetHandler.DefaultFileName)));
    }

    [Fact]
    public async Task TestReadItBack()
    {
        await TestItCanWrite();

        var content = await TestCreateGroupedOrderbook.GetFileContents("grouped_orderbook_btcusd.json");
        using var orderbookGroupedWrapper = JsonSerializer.Deserialize<GroupedOrderbookDetails>(content,
            FtxGroupedOrderBookWebsocket.OrderBookJsonSerializerOptions);

        var pairh = PairHasher.Hash(orderbookGroupedWrapper!.Market);

        using (Stream fileStream =
               System.IO.File.OpenRead(Path.Combine(_tmpPath, SaveRegroupedOrderbookToParquetHandler.DefaultFileName)))
        {
            using (var parquetReader = await ParquetReader.CreateAsync(fileStream))
            {
                DataField[] dataFields = parquetReader.Schema.GetDataFields();
                Assert.NotEmpty(dataFields);
                Assert.Equal(new string[] { "time", "market" },
                    dataFields.Select(field => field.Name).Take(2));

                Assert.Equal(1, parquetReader.RowGroupCount);
                for (var i = 0; i < parquetReader.RowGroupCount; i++)
                {
                    using var groupReader = parquetReader.OpenRowGroupReader(i);
                    ArrayList<DataColumn> columns = new();
                    foreach (var dataField in dataFields)
                    {
                        columns.Add(await groupReader.ReadColumnAsync(dataField, CancellationToken.None));
                    }
                    Assert.Equal(new dynamic[] { pairh },
                        columns[1].Data); // it must conserve order too
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