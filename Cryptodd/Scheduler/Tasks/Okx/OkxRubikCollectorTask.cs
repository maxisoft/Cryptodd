using System.Diagnostics;
using Cryptodd.Okx.Collectors.Options;
using Cryptodd.Okx.Collectors.RubikStat;
using Cryptodd.Okx.Collectors.Swap;
using Cryptodd.Okx.Orderbooks;
using Cryptodd.Okx.Websockets.Pool;
using Lamar;
using Maxisoft.Utils.Collections.Queues.Specialized;
using Microsoft.Extensions.Configuration;
using Polly;
using Serilog;

namespace Cryptodd.Scheduler.Tasks.Okx;

// ReSharper disable once UnusedType.Global
public class OkxRubikCollectorTask : BasePeriodicScheduledTask
{
    private AsyncPolicy _retryPolicy;
    private readonly BoundedDeque<CancellationTokenSource> _cancellationTokenSources = new(8);
    private IRubikStatDataCollector? _rubikDataCollector;

    public OkxRubikCollectorTask(IContainer container, ILogger logger, IConfiguration configuration) : base(
        logger,
        configuration, container)
    {
        _retryPolicy = Policy.NoOpAsync();
        Period = TimeSpan.FromSeconds(15);
        Section = Configuration.GetSection("Okx:Collector:Rubik:Task");
        OnConfigurationChange();
    }

    public override async Task Execute(CancellationToken cancellationToken)
    {
        var cts = CreateCancellationTokenSource(cancellationToken);

        IRubikStatDataCollector OptionDataCollector()
        {
            if (_rubikDataCollector is null)
            {
                _rubikDataCollector = Container.GetInstance<IRubikStatDataCollector>();
            }

            return _rubikDataCollector;
        }

        void OnDownloadComplete()
        {
            var ctx = TaskRunningContext;
            if (Const.IsDebug && Debugger.IsAttached)
            {
                return;
            }

            if (ctx is null)
            {
                return;
            }


            ctx.Hooks.TimeElapsed = new Lazy<TimeSpan?>(ctx.TimeElapsed);
        }

        var mainTask = _retryPolicy.ExecuteAsync(
            _ => OptionDataCollector().Collect(OnDownloadComplete, cts.Token),
            cancellationToken);

        try
        {
            await mainTask.ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Logger.Debug(e, "");
            var wasCancelled = cts.IsCancellationRequested;
            if (!wasCancelled)
            {
                cts.Cancel();
            }

            if (_rubikDataCollector is not null)
            {
                await _rubikDataCollector.DisposeAsync().ConfigureAwait(false);
            }

            _rubikDataCollector = null;
        }

        if (!mainTask.IsCanceled || cts.IsCancellationRequested)
        {
            await mainTask.ConfigureAwait(false);
        }

        if (!mainTask.IsCompleted)
        {
            await mainTask.ConfigureAwait(false);
        }

        mainTask.Dispose();

        cts.Cancel();
    }

    protected virtual CancellationTokenSource CreateCancellationTokenSource(CancellationToken cancellationToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            cts.CancelAfter(Period);
            while (_cancellationTokenSources.IsFull)
            {
                if (_cancellationTokenSources.TryPopFront(out var old))
                {
                    old.Dispose();
                }
            }

            var stable = false;
            while (!stable)
            {
                stable = true;
                foreach (var source in _cancellationTokenSources)
                {
                    try
                    {
                        source.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                        _cancellationTokenSources.Remove(source);
                        stable = false;
                        break;
                    }
                }
            }


            _cancellationTokenSources.Add(cts);
            return cts;
        }
        catch
        {
            cts.Dispose();
            throw;
        }
    }

    private void ConfigureRetryPolicy()
    {
        var maxRetry = Section.GetValue("MaxRetry", 3);
        _retryPolicy = Policy.Handle<Exception>(static e => e is not OperationCanceledException)
            .WaitAndRetryAsync(maxRetry, i => TimeSpan.FromSeconds(1 + i));
    }

    protected override void OnConfigurationChange(object? obj)
    {
        base.OnConfigurationChange(obj);
        ConfigureRetryPolicy();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var cts in _cancellationTokenSources)
            {
                cts.Dispose();
            }

            _cancellationTokenSources.Clear();
        }

        base.Dispose(disposing);
    }
}