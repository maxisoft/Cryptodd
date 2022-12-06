using System.Diagnostics;
using System.Text.Json.Nodes;
using Cryptodd.Pairs;
using Lamar;
using Maxisoft.Utils.Collections.LinkedLists;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Disposables;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Cryptodd.Binance.Orderbook.Websocket;

public class BinanceOrderbookCollectorOptions
{
    public int SymbolsExpiry { get; set; } = TimeSpan.FromMinutes(15).Milliseconds;
}

public class BinanceOrderbookCollector
{
    private readonly IContainer _container;
    protected INestedContainer? NestedContainer { get; set; }
    private readonly DisposableManager _disposableManager = new ();
    private IBinancePublicHttpApi _httpApi;
    private readonly ILogger _logger;
    private SemaphoreSlim _semaphoreSlim = new(1, 1);
    private ArrayList<string> _cachedSymbols = new();
    private readonly Stopwatch _cachedSymbolsStopwatch = new();
    protected BinanceOrderbookCollectorOptions Options { get; init; } = new ();

    protected string ConfigurationSection { get; init; } = "Binance:OrderbookCollector";
    protected string PairFilterName { get; init; } = "Binance:Orderbook";

    public BinanceOrderbookCollector(IContainer container, IBinancePublicHttpApi httpApi, ILogger logger, IConfiguration configuration)
    {
        _container = container;
        _httpApi = httpApi;
        _logger = logger;
        configuration.GetSection(ConfigurationSection).Bind(Options);
    }

    protected virtual async Task Setup(CancellationToken cancellationToken)
    {
        // handle task
        // http api call to update ob
        // dispose any ReferenceCounterDisposable lately 

        var updateSymbolsTask = UpdateCachedSymbols(cancellationToken);
        if (_cachedSymbols.Count <= 0 || NestedContainer is null)
        {
            await updateSymbolsTask.ConfigureAwait(false);
        }

        var symbols = _cachedSymbols;

        LinkedListAsIList<BinanceOrderbookWebsocket> CreateWebsockets(ArrayList<string> symbols)
        {
            NestedContainer ??= _container.GetNestedContainer();
            var res = new LinkedListAsIList<BinanceOrderbookWebsocket>();
            var ws = NestedContainer.GetRequiredService<BinanceOrderbookWebsocket>();
            res.AddLast(ws);
            foreach (var symbol in symbols)
            {
                while (!ws.AddDepthSymbol(symbol))
                {
                    ws = NestedContainer.GetRequiredService<BinanceOrderbookWebsocket>();
                    res.AddLast(ws);
                }
            }

            return res;
        }

        var webSockets = CreateWebsockets(symbols);
        _logger.Debug("Created {Count} websockets for {SymbolCount} with max {Limit} channels", webSockets.Count, symbols.Count, webSockets.FirstOrDefault()?.Options.MaxStreamCountSoftLimit);



        if (!updateSymbolsTask.IsCompleted)
        {
            await updateSymbolsTask;
        }
    }

    private async Task UpdateCachedSymbols(CancellationToken cancellationToken)
    {
        if (!_cachedSymbolsStopwatch.IsRunning || _cachedSymbolsStopwatch.ElapsedMilliseconds > Options.SymbolsExpiry)
        {
            var symbols = await ListSymbols(cancellationToken).ConfigureAwait(false);
            NestedContainer ??= _container.GetNestedContainer();
            var pairFilterLoader = NestedContainer.GetRequiredService<IPairFilterLoader>();
            var pairFilter = await pairFilterLoader.GetPairFilterAsync(PairFilterName, cancellationToken);

            static ArrayList<string> FilterSymbols(ArrayList<string> symbols, IPairFilter pairFilter)
            {
                var res = new ArrayList<string>(symbols.Count);
                // ReSharper disable once ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator
                foreach (var symbol in symbols)
                {
                    if (pairFilter.Match(symbol))
                    {
                        res.Add(symbol);
                    }
                }

                return res;
            }

            _cachedSymbols = FilterSymbols(symbols, pairFilter);
            _cachedSymbols.ShrinkToFit();
            _cachedSymbolsStopwatch.Restart();
        }
    }

    internal async Task<ArrayList<string>> ListSymbols(CancellationToken cancellationToken)
    {
        var exchangeInfo =
            await _httpApi.GetExchangeInfoAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        ArrayList<string> res = new();
        // ReSharper disable InvertIf
        if (exchangeInfo["symbols"] is JsonArray symbols)
        {
            res.EnsureCapacity(symbols.Count);
            foreach (var symbolInfoNode in symbols)
            {
                if (symbolInfoNode is JsonObject symbolInfo)
                {
                    if (symbolInfo["symbol"] is JsonValue symbol && symbolInfo["status"] is JsonValue status && status.GetValue<string>() == "TRADING")
                    {
                        res.Add(symbol.GetValue<string>());
                    }
                }
            }
        }
        // ReSharper restore InvertIf

        return res;
    }

    public async Task CollectOrderBook()
    {
        //Setup();
    }
}