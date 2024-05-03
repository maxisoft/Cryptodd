using System.Diagnostics;
using Cryptodd.IoC;
using Cryptodd.Okx.Models;
using JasperFx.Core;
using Lamar;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Logic;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cryptodd.Okx.Collectors.Swap;

public interface ISwapDataCollector : IDisposable, IAsyncDisposable
{
    public Task<ISet<OkxInstrumentIdentifier>> Collect(Action<ISet<OkxInstrumentIdentifier>>? beforeSerialisation,
        CancellationToken cancellationToken);

    public bool Disposed { get; }
}

public sealed class SwapDataCollector : IService, ISwapDataCollector
{
    private readonly IContainer _container;
    private readonly ILogger _logger;
    private readonly IBackgroundSwapDataCollector _backgroundSwapDataCollector;
    private Task? _collectLoop;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Lazy<SwapDataWriter> _swapDataWriter;
    private AtomicBoolean _disposed = new AtomicBoolean();

    public SwapDataCollector(ILogger logger, IContainer container, IConfiguration configuration,
        IBackgroundSwapDataCollector backgroundSwapDataCollector, Lazy<SwapDataWriter> swapDataWriter, Boxed<CancellationToken> cancellationToken)
    {
        _logger = logger.ForContext(GetType());
        _container = container;
        _backgroundSwapDataCollector = backgroundSwapDataCollector;
        _swapDataWriter = swapDataWriter;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    }

    public async Task<ISet<OkxInstrumentIdentifier>> Collect(Action<ISet<OkxInstrumentIdentifier>>? beforeSerialisation,
        CancellationToken cancellationToken)
    {
        using var cts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cancellationTokenSource.Token);
        cancellationToken = cts.Token;
        if (_collectLoop?.IsCompleted ?? true)
        {
            if (_collectLoop?.IsFaulted ?? false)
            {
                _logger.Warning(_collectLoop.Exception, "Previous swap collect loop failed");
            }

            _collectLoop = _backgroundSwapDataCollector.CollectLoop(_cancellationTokenSource.Token);
        }

        var repo = _container.GetRequiredService<ISwapDataRepository>();

        var symbols = repo.FundingRates.Keys.ToHashSet();
        symbols.IntersectWith(repo.OpenInterests.Keys);
        symbols.IntersectWith(repo.Tickers.Keys);
        symbols.IntersectWith(repo.MarkPrices.Keys);

        beforeSerialisation?.Invoke(symbols);
        if (!symbols.Any())
        {
            return symbols;
        }

        ArrayList<OkxInstrumentIdentifier> symbolsWithError = new();
        await Parallel.ForEachAsync(symbols, cancellationToken, async (identifier, token) =>
        {
            Debug.Assert(identifier.Type.Equals("swap", StringComparison.OrdinalIgnoreCase));
            var fr = repo.FundingRates[identifier];
            var oi = repo.OpenInterests[identifier];
            var ticker = repo.Tickers[identifier];
            var markPrice = repo.MarkPrices[identifier];
            try
            {
                await _swapDataWriter.Value.WriteAsync(identifier.Id, (oi, fr, ticker, markPrice), fr.Date, token).ConfigureAwait(true);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error while writing {Symbol} swap data to file", identifier.Id);
                symbolsWithError.Add(identifier);
#if DEBUG
                throw;
#endif
            }
        }).ConfigureAwait(false);
        if (symbolsWithError.Count > 0)
        {
            symbols.ExceptWith(symbolsWithError);
        }

        return symbols;
    }

    public bool Disposed => _disposed.Value;

    public void Dispose()
    {
        var disposing = false;
        if (_disposed.FalseToTrue())
        {
            _cancellationTokenSource.Cancel();
            disposing = true;
        }

        _collectLoop?.Dispose();
        _collectLoop = null;
        if (disposing && _swapDataWriter.IsValueCreated)
        {
            _swapDataWriter.Value.Dispose();
        }

        _cancellationTokenSource.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed.Value)
        {
            await _cancellationTokenSource.CancelAsync();
        }

        if (_collectLoop is { IsCompleted: false })
        {
            try
            {
                await _collectLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
        }

        Dispose();
    }
}