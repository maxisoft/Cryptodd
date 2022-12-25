using Cryptodd.Binance.Orderbooks.Websockets;
using MathNet.Numerics.Random;
using Maxisoft.Utils.Collections.LinkedLists;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Disposables;
using Serilog.Events;

namespace Cryptodd.Binance.Orderbooks;

public abstract class BaseBinanceWebsocketCollection<TBinanceOrderbookWebsocket, TOptions> : IAsyncDisposable
    where TOptions : BaseBinanceOrderbookWebsocketOptions, new()
    where TBinanceOrderbookWebsocket : BaseBinanceOrderbookWebsocket<TOptions>
{
    protected Xoshiro256StarStar Random { get; } = new(threadSafe: true);
    protected const int MaxNumberOfConnections = 128;

    protected LinkedListAsIList<TBinanceOrderbookWebsocket> Websockets { get; } = new();

    protected DisposableManager DisposableManager { get; } = new();

    protected HashSet<string> SlowToUpdateSymbol { get; } = new();

    public int SymbolsHash { get; private set; }
    public int Count => Websockets.Count;

    protected static int GuessIdealNumberOfConnection(int numSymbols) =>
        numSymbols < 10 ? 1 : Math.Min(int.Log2(numSymbols) + 1, MaxNumberOfConnections);

    protected static T DoCreate<T>(ArrayList<string> symbols, Func<TBinanceOrderbookWebsocket> factory)
        where T : BaseBinanceWebsocketCollection<TBinanceOrderbookWebsocket, TOptions>, new()
    {
        var res = new T();
        var websockets = new TBinanceOrderbookWebsocket?[GuessIdealNumberOfConnection(symbols.Count)];
        var h = new HashCode();
        var i = 0;
        try
        {
            if (websockets.Length > 0)
            {
                ref var ws = ref websockets[0];
                foreach (var symbol in symbols)
                {
                    var tryCount = 0;
                    do
                    {
                        ws = ref websockets[i % websockets.Length];
                        i++;
                        if (ws is null)
                        {
                            ws = factory();
                        }

                        if (tryCount++ > websockets.Length)
                        {
                            throw new ArgumentException(
                                $"unable to add depth symbol {symbol} of out {symbols.Count} symbols probably because there's not enough active websocket",
                                nameof(symbols));
                        }
                    } while (!ws.AddDepthSymbol(symbol));

                    h.Add(symbol);
                }
            }


            res.SymbolsHash = h.ToHashCode();
            var list = res.Websockets;
            foreach (var websocket in websockets)
            {
                if (websocket is null)
                {
                    continue;
                }

                list.AddLast(websocket);
                res.DisposableManager.LinkDisposableAsWeak(websocket);
            }
        }
        catch
        {
            foreach (var websocket in websockets)
            {
                websocket?.Dispose();
            }

            throw;
        }

        return res;
    }


    public async Task Start()
    {
        var websockets = Websockets.ToArray();
        if (websockets.Any(ws => !ws.IsClosed))
        {
            throw new Exception("at least 1 websocket is already running.");
        }

        var tasks = new Task[websockets.Length];
        using var cts = new CancellationTokenSource();
        for (var i = 0; i < websockets.Length; i++)
        {
            var websocket = websockets[i];
            tasks[i] = websocket.ReceiveLoop(cts.Token);
        }

        try
        {
            var monitor = MonitorWebsockets(cts.Token);
            await Task.WhenAny(tasks);
            cts.Cancel();
            await monitor;
        }
        catch
        {
            foreach (var websocket in websockets)
            {
                websocket.StopReceiveLoop();
            }

            throw;
        }
        finally
        {
            cts.Cancel();
        }
    }

    private async Task MonitorWebsockets(CancellationToken cancellationToken)
    {
        var startDate = DateTimeOffset.Now;
        var stop = false;
        while (!stop && !cancellationToken.IsCancellationRequested)
        {
            var i = 0;
            var now = DateTimeOffset.Now;
            foreach (var websocket in Websockets)
            {
                var globalLastCall = websocket.DepthWebsocketStats.LastCall;
                const int maxInactivitySecond = 20;
                if ((now - globalLastCall).Duration() > TimeSpan.FromSeconds(maxInactivitySecond))
                {
                    websocket.StopReceiveLoop($"due to inactivity for {maxInactivitySecond} seconds",
                        stop ? LogEventLevel.Verbose : LogEventLevel.Warning);
                    stop = true;
                }

                if (!stop && (globalLastCall - startDate).Duration() > TimeSpan.FromSeconds(120))
                {
                    var symbols = websocket.TrackedDepthSymbols;
                    var issueCount = 0;
                    foreach (var symbol in symbols)
                    {
                        if (websocket.IsBlacklistedSymbol(symbol))
                        {
                            continue;
                        }

                        var isSlow = SlowToUpdateSymbol.Contains(symbol);

                        if ((now - websocket.DepthWebsocketStats.GetStatsForSymbol(symbol).LastCall).Duration() <=
                            TimeSpan.FromMinutes(1) * (isSlow ? 5 : 1))
                        {
                            continue;
                        }

                        issueCount++;
                        if (3 * issueCount > symbols.Count + 1)
                        {
                            websocket.StopReceiveLoop(
                                $"due to too much inactivity: {issueCount}/{symbols.Count} issues last minute",
                                stop ? LogEventLevel.Verbose : LogEventLevel.Warning);
                            stop = true;
                            break;
                        }

                        if (!isSlow)
                        {
                            SlowToUpdateSymbol.Add(symbol);
                        }

                        // try to fix slow issue by pushing symbol to other websocket (should recreate the connection automatically)
                        var websockets = Websockets.ToArray();
                        var issueFixed = false;
                        var shift = Random.Next(websockets.Length);
                        for (var j = 0; j < websockets.Length; j++)
                        {
                            var otherWs = websockets[(i + j + shift) % websockets.Length];
                            if (ReferenceEquals(websocket, otherWs))
                            {
                                continue;
                            }

                            if (otherWs.IsBlacklistedSymbol(symbol))
                            {
                                continue;
                            }

                            if (!otherWs.AddDepthSymbol(symbol))
                            {
                                continue;
                            }

                            issueFixed = true;
                            break;
                        }

                        if (!issueFixed)
                        {
                            websocket.StopReceiveLoop($"due to no activity for {symbol} last minute",
                                stop ? LogEventLevel.Verbose : LogEventLevel.Warning);
                            stop = true;
                            break;
                        }

                        websocket.BlacklistSymbol(symbol);
                    }
                }

                i++;
            }

            await Task.Delay(10_000, cancellationToken).ConfigureAwait(false);
        }
    }

    public bool SymbolHashMatch<TEnumerable>(in TEnumerable symbols) where TEnumerable : IEnumerable<string>
    {
        var h = new HashCode();
        foreach (var symbol in symbols)
        {
            h.Add(symbol);
        }

        return SymbolsHash == h.ToHashCode();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var websocket in Websockets)
        {
            await websocket.DisposeAsync();
            DisposableManager.UnlinkDisposable(websocket);
        }

        Websockets.Clear();
        DisposableManager.Dispose();
        SymbolsHash = 0;
        SlowToUpdateSymbol.Clear();

        GC.SuppressFinalize(this);
    }
}