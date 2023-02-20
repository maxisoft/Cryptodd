using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Cryptodd.Ftx.Orderbooks;
using Cryptodd.IoC;
using Cryptodd.IoC.Registries.Customs;
using Cryptodd.Okx.Collectors;
using Cryptodd.Okx.Collectors.Swap;
using Cryptodd.Pairs;
using Lamar;
using Maxisoft.Utils.Collections.Dictionaries;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using xRetry;
using Xunit;
using Skip = xRetry.Skip;

namespace Cryptodd.Tests.Okx.Collectors;

public class BackgroundSwapDataCollectorTests : IDisposable
{
    private readonly string _tmpPath;
    private readonly Container _container;

    public void Dispose()
    {
        Directory.Delete(_tmpPath, true);
        _container.Dispose();
    }

    public BackgroundSwapDataCollectorTests()
    {
        _tmpPath = Path.GetTempPath();
        _tmpPath = Path.Combine(_tmpPath, Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tmpPath);
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

            PostConfigure = PostConfigure
        });
    }

    private void PostConfigure(ServiceRegistry obj) { }


    [RetryFact(1)]
    public async Task TestReal()
    {
        var cancellationToken = _container.GetRequiredService<Boxed<CancellationToken>>().Value;

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(15_000);
        cancellationToken = cts.Token;

        var backgroundSwapDataCollector = _container.GetRequiredService<BackgroundSwapDataCollector>();

        var task = backgroundSwapDataCollector.CollectLoop(cancellationToken);

        var repo = _container.GetRequiredService<ISwapDataRepository>();

        async Task PollResult()
        {
            while (repo.FundingRates.IsEmpty || repo.OpenInterests.IsEmpty)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        try
        {
            await Task.WhenAny(PollResult(), task);
        }
        catch (HttpRequestException e)
        {
            Skip.Always(e.ToStringDemystified());
            throw;
        }
        catch (WebSocketException e)
        {
            Skip.Always(e.ToStringDemystified());
            throw;
        }


        Assert.NotEmpty(repo.FundingRates);
        Assert.NotEmpty(repo.OpenInterests);


        do
        {
            try
            {
                await Task.Delay(1000, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        } while (!cancellationToken.IsCancellationRequested &&
                 Math.Abs(repo.FundingRates.Count - repo.OpenInterests.Count) > 1);

        Assert.True(Math.Abs(repo.FundingRates.Count - repo.OpenInterests.Count) <= 5);
        cts.Cancel();

        try
        {
            await task;
        }
        catch (OperationCanceledException) { }
    }
}