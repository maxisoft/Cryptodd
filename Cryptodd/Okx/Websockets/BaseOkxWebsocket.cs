using System.Diagnostics;
using System.Net.WebSockets;
using Cryptodd.Binance.Models;
using Cryptodd.Http;
using Cryptodd.Okx.Limiters;
using Cryptodd.Okx.Models;
using Cryptodd.Okx.Websockets.Subscriptions;
using Cryptodd.Utils;
using Cryptodd.Websockets;
using Maxisoft.Utils.Algorithms;
using Maxisoft.Utils.Collections.Lists;
using Maxisoft.Utils.Disposables;
using Maxisoft.Utils.Logic;
using Maxisoft.Utils.Objects;
using Serilog;

namespace Cryptodd.Okx.Websockets;

public abstract class BaseOkxWebsocket<TData, TOptions> : BaseWebsocket<TData, TOptions>
    where TOptions : BaseOkxWebsocketOptions, new()
    where TData : PreParsedOkxWebSocketMessage, new()
{
    protected IOkxLimiterRegistry LimiterRegistry { get; }
    private readonly DisposableManager _disposableManager = new();

    protected BaseOkxWebsocket(IOkxLimiterRegistry limiterRegistry, ILogger logger,
        IClientWebSocketFactory webSocketFactory,
        Boxed<CancellationToken> cancellationToken) : base(logger, webSocketFactory, cancellationToken)
    {
        LimiterRegistry = limiterRegistry;
        _subscriptionsLimiter = LimiterRegistry.CreateNewWebsocketSubscriptionLimiter();
        _disposableManager.LinkDisposable(_subscriptionsLimiter.NewDecrementOnDispose());
    }

    protected override TOptions Options { get; set; } = new();
    protected override ClientWebSocket? WebSocket { get; set; }

    protected override Uri CreateUri() => new(Options.BaseAddress);

    public override async ValueTask<bool> ConnectIfNeeded(CancellationToken cancellationToken)
    {
        if (IsClosed)
        {
            return await LimiterRegistry.WebsocketConnectionLimiter.WaitForLimit(
                    _ => base.ConnectIfNeeded(cancellationToken).AsTask(), cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        return false;
    }


    public async ValueTask<bool> Ping(CancellationToken cancellationToken)
    {
        var ws = WebSocket;
        if (ws?.State is not WebSocketState.Open)
        {
            return false;
        }

        await ws.SendAsync(OkxWebsocketMessageHelper.PingMessage, WebSocketMessageType.Text, true,
            cancellationToken).ConfigureAwait(false);
        return true;
    }


    protected long LastMessageDate { get; set; } = DateTimeOffset.UnixEpoch.ToUnixTimeMilliseconds();

    protected static long GetUnixTimeMilliseconds() => DateTimeOffset.Now.ToUnixTimeMilliseconds();

    protected override ReceiveMessageFilter FilterReceivedMessage(Span<byte> message,
        ValueWebSocketReceiveResult receiveResult, ref TData? data)
    {
        if (receiveResult is { EndOfMessage: true, Count: > 0, MessageType: WebSocketMessageType.Text })
        {
            LastMessageDate = Math.Max(GetUnixTimeMilliseconds(), LastMessageDate);
        }

        if (receiveResult.EndOfMessage && message.SequenceEqual(OkxWebsocketMessageHelper.PongMessage.Span))
        {
            return ReceiveMessageFilter.Break | ReceiveMessageFilter.Ignore | ReceiveMessageFilter.ConsumeTheRest;
        }


        if (!PreParsedOkxWebSocketMessage.TryParse(message, out var newData))
        {
            return ReceiveMessageFilter.Ignore | ReceiveMessageFilter.ConsumeTheRest;
        }

        data ??= new TData();
        newData.CopyTo(data);

        return ReceiveMessageFilter.ExtractedData;
    }

    protected virtual void OnSubscribe(TData data)
    {
        switch (data.ArgChannel)
        {
            case "books":
            case "books5":
            case "bbo-tbt":
            case "books-l2-tbt":
            case "books50-l2-tbt":
                Subscriptions.ConfirmSubscription(new OkxOrderbookSubscription(data.ArgChannel, data.ArgInstrumentId));
                break;

            default:
                Logger.Warning("Unhandled subscribed channel {Channel}", data.ArgChannel);
                break;
        }
    }

    protected virtual void OnUnsubscribe(TData data)
    {
        switch (data.ArgChannel)
        {
            case "books":
            case "books5":
            case "bbo-tbt":
            case "books-l2-tbt":
            case "books50-l2-tbt":
                Subscriptions.ForceRemove(new OkxOrderbookSubscription(data.ArgChannel, data.ArgInstrumentId));
                break;

            default:
                Logger.Warning("Unhandled unsubscribed channel {Channel} {Instrument}", data.ArgChannel,
                    data.ArgInstrumentId);
                break;
        }
    }

    protected virtual void OnError(in TData data)
    {
        Logger.Warning("Error message received {Code} {Message}", data.Code, data.Message);
        if (Options.CloseOnErrorMessage ?? true)
        {
            Close("error message with code " + data.Code);
        }
    }

    protected override Task DispatchMessage(TData? data, ReadOnlyMemory<byte> memory,
        CancellationToken cancellationToken)
    {
        switch (data?.Event)
        {
            case "subscribe":
                OnSubscribe(data);
                break;
            case "unsubscribe":
                OnUnsubscribe(data);
                break;
            case "error":
                OnError(data);
                break;
        }

        return Task.CompletedTask;
    }

    public async Task EnsureConnectionActivityTask(CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, CancellationToken);
        cancellationToken = cts.Token;
        while (!cancellationToken.IsCancellationRequested)
        {
            var interval = checked((int)Options.PingInterval.TotalMilliseconds);
            if (interval <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(Options.PingInterval), interval,
                    "invalid ping interval options");
            }

            var now = GetUnixTimeMilliseconds();
            var diff = now - LastMessageDate;
            diff = Math.Abs(diff);
            if (diff > interval)
            {
                await Ping(cancellationToken).ConfigureAwait(false);
                LastMessageDate = now = GetUnixTimeMilliseconds();
            }


            await Task.Delay((int)(LastMessageDate + interval).Clamp(now + interval / 3, now + interval),
                cancellationToken).ConfigureAwait(false);
        }

        cts.Cancel();
    }

    protected OkxSubscriptions Subscriptions { get; } = new();
    protected SemaphoreSlim SubscriptionsSemaphore = new(1, 1);
    private readonly ReferenceCounterDisposable<OkxLimiter> _subscriptionsLimiter;

    protected ref readonly OkxLimiter SubscriptionsLimiter => ref _subscriptionsLimiter.ValueRef;

    private static int WriteBytes(Span<byte> buffer, ReadOnlySpan<byte> b) =>
        OkxSubscription.WriteBytes(buffer, b);
    protected virtual Span<byte> CreateSubscriptionPayload<TEnumerable>(TEnumerable enumerable, Span<byte> buffer,
        ref int counter, bool isSubscription)
        where TEnumerable : IEnumerable<OkxSubscription>
    {
        var c = 0;
        Span<byte> newSpan;

        {
            var bytesWritten = WriteBytes(buffer,
                isSubscription ? "{\"op\":\"subscribe\",\"args\":["u8 : "{\"op\":\"unsubscribe\",\"args\":["u8);
            if (bytesWritten <= 0)
            {
                return Span<byte>.Empty;
            }

            c += bytesWritten;
        }

        var checkPoint = c;
        
        foreach (var subscription in enumerable)
        {
            newSpan = isSubscription ? subscription.WriteSubscribePayload(buffer[c..]) : subscription.WriteUnsubscribePayload(buffer[c..]);
            if (newSpan.IsEmpty)
            {
                return newSpan;
            }

            c += newSpan.Length;
            if (c >= buffer.Length)
            {
                return Span<byte>.Empty;
            }

            buffer[c] = (byte)',';
            c++;
            counter++;
        }

        if (c > 0 && buffer[c - 1] == ',')
        {
            c--;
        }

        if (c == checkPoint)
        {
            return Span<byte>.Empty;
        }
        
        {
            var bytesWritten = WriteBytes(buffer[c..], "]}"u8);
            if (bytesWritten <= 0)
            {
                return Span<byte>.Empty;
            }

            c += bytesWritten;
        }

        return buffer[..c];
    }

    public async Task<int> Subscribe<T>(CancellationToken cancellationToken, params T[] subscriptions)
        where T : OkxSubscription
        => await Subscribe(subscriptions, cancellationToken);

    private async Task<int> DoSubscribe<TCollection>(TCollection subscriptions, CancellationToken cancellationToken)
        where TCollection : IReadOnlyCollection<OkxSubscription>
    {
        using var decrementOnDispose = _subscriptionsLimiter.NewDecrementOnDispose();

        var res = -1;
        var release = new AtomicBoolean();
        try
        {
            while (res < 0 && !Disposed.Value)
            {
                if (SubscriptionsLimiter.AvailableCount >= subscriptions.Count)

                {
                    if (release.TrueToFalse())
                    {
                        SubscriptionsSemaphore.Release();
                    }
                }
                else if (release.FalseToTrue())
                {
                    await SubscriptionsSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                }


                res = await SubscriptionsLimiter.WaitForLimit(
                    async parameter =>
                    {
                        if (release.FalseToTrue() && !await SubscriptionsSemaphore.WaitAsync(0, cancellationToken)
                                .ConfigureAwait(false))
                        {
                            release.TrueToFalse();
                            parameter.RegistrationCount = 0;
                            return -1;
                        }

                        try
                        {
                            var subscribeCount = await DoSubscribeOrUnsubscribe(true, subscriptions, cancellationToken)
                                .ConfigureAwait(false);
                            Debug.Assert(subscribeCount >= 0);
                            Debug.Assert(subscribeCount <= parameter.Count);
                            parameter.RegistrationCount = subscribeCount;
                            return subscribeCount;
                        }
                        finally
                        {
                            if (release.TrueToFalse())
                            {
                                SubscriptionsSemaphore.Release();
                            }
                        }
                    },
                    subscriptions.Count,
                    cancellationToken: cancellationToken);
            }
        }
        finally
        {
            if (res > 0)
            {
                Interlocked.Add(ref _accumulatedSubscriptionCount, res);
            }

            if (release.TrueToFalse())
            {
                SubscriptionsSemaphore.Release();
            }
        }

        return res;
    }

    public async Task<int> Subscribe<TCollection>(TCollection subscriptions, CancellationToken cancellationToken)
        where TCollection : IReadOnlyCollection<OkxSubscription>
    {
        if (Disposed)
        {
            return -1;
        }
        try
        {
            return await DoSubscribe(subscriptions, cancellationToken);
        }
        catch (ObjectDisposedException)
        {
            if (Disposed)
            {
                return -1;
            }

            throw;
        }
    }


    protected virtual async Task<int> DoSubscribeOrUnsubscribe<TCollection>(bool isSubscription,
        TCollection subscriptions,
        CancellationToken cancellationToken)
        where TCollection : IReadOnlyCollection<OkxSubscription>
    {
        using var buffer = MemoryPool.Rent(Options.SubscribeMaxBytesLength);
        var res = 0;

        var requestObject = isSubscription ? "subscription" : "unsubscription";

        IEnumerable<OkxSubscription> SubscriptionIterator()
        {
            foreach (var subscription in subscriptions)
            {
                if (Subscriptions.GetState(in subscription) is OkxSubscriptionSate.None)
                {
                    yield return subscription;
                }
            }
        }

        IEnumerable<OkxSubscription> UnsubscriptionIterator()
        {
            foreach (var subscription in subscriptions)
            {
                if (Subscriptions.GetState(in subscription) is OkxSubscriptionSate.Subscribed)
                {
                    yield return subscription;
                }
            }
        }

        var payloadLength =
            CreateSubscriptionPayload(isSubscription ? SubscriptionIterator() : UnsubscriptionIterator(),
                    buffer.Memory.Span[..Options.SubscribeMaxBytesLength],
                    ref res, isSubscription)
                .Length;
        if (payloadLength <= 0)
        {
            if (isSubscription)
            {
                throw new ArgumentException("subscription list doesn't contains any valid subscription");
            }
            // this is an error due to concurrency most likely
            return -2;
        }

        var ws = WebSocket;
        if (ws is null || IsClosed)
        {
            return 0;
        }

        try
        {
            await ws.SendAsync(buffer.Memory[..payloadLength], WebSocketMessageType.Text, true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            Close("unable to send " + requestObject + 's');
            throw;
        }


        ArrayList<Exception> exceptions = new();

        var now = DateTimeOffset.Now;
        if (isSubscription)
        {
            foreach (var subscription in subscriptions)
            {
                try
                {
                    Subscriptions.PendingSubscription(in subscription, now);
                }
                catch (ArgumentException e)
                {
                    exceptions.Add(e);
                }
            }
        }
        else
        {
            foreach (var unsubscription in subscriptions)
            {
                try
                {
                    Subscriptions.Unsubscribe(in unsubscription, checkWasSubscribed: false);
                }
                catch (ArgumentException e)
                {
                    exceptions.Add(e);
                }
            }
        }


        if (exceptions.Any())
        {
            if (Options.CloseOnInvalidSubscription ?? true)
            {
                Close("invalid " + requestObject + "(s)");
            }

            throw new AggregateException(exceptions);
        }

        return res;
    }

    public async Task<int> UnSubscribe<T>(CancellationToken cancellationToken, params T[] subscriptions)
        where T : OkxSubscription
        => await UnSubscribe(subscriptions, cancellationToken);

    public async Task<int> UnSubscribe<TCollection>(TCollection subscriptions, CancellationToken cancellationToken)
        where TCollection : IReadOnlyCollection<OkxSubscription>
    {
        if (Disposed)
        {
            return -1;
        }

        try
        {
            using (await SubscriptionsSemaphore.WaitAndGetDisposableAsync(cancellationToken).ConfigureAwait(false))
            {
                return await DoSubscribeOrUnsubscribe(false, subscriptions, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (ObjectDisposedException)
        {
            if (!Disposed.Value)
            {
                throw;
            }

            return -1;
        }
    }


    protected override bool Dispose(bool disposing)
    {
        disposing &= base.Dispose(disposing);
        // ReSharper disable once InvertIf
        if (disposing)
        {
            SubscriptionsSemaphore.Dispose();
            _disposableManager.Dispose();
        }


        return disposing;
    }

    public int SubscriptionCount => Subscriptions.Count;

    private int _accumulatedSubscriptionCount;
    public int AccumulatedSubscriptionCount => _accumulatedSubscriptionCount;

    #region SwapWebSocket

    internal async Task<bool> SwapWebSocket<T, TData2, TOptions2>(T other, CancellationToken cancellationToken)
        where T : BaseOkxWebsocket<TData2, TOptions2>
        where TData2 : PreParsedOkxWebSocketMessage, new()
        where TOptions2 : BaseOkxWebsocketOptions, new()
    {
        if (SubscriptionCount > 0 || other.SubscriptionCount > 0)
        {
            return false;
        }

        try
        {
            using var s1 = await SubscriptionsSemaphore.WaitAndGetDisposableAsync(cancellationToken);
            using var s3 = await other.SubscriptionsSemaphore.WaitAndGetDisposableAsync(cancellationToken);
            if (SubscriptionCount > 0 || other.SubscriptionCount > 0)
            {
                return false;
            }

            using var s0 = await SemaphoreSlim.WaitAndGetDisposableAsync(cancellationToken);
            using var s2 = await other.SemaphoreSlim.WaitAndGetDisposableAsync(cancellationToken);

            (WebSocket, other.WebSocket) = (other.WebSocket, WebSocket);
            return true;
        }
        catch (ObjectDisposedException e)
        {
            Logger.Verbose(e, "semaphore got disposed before WaitAsync() call");
            return false;
        }
    }

    internal bool SwapWebSocket(ref ClientWebSocket? ws)
    {
        if (SubscriptionCount > 0)
        {
            return false;
        }

        try
        {
            SemaphoreSlim.Wait();
        }
        catch (ObjectDisposedException e)
        {
            Logger.Verbose(e, "semaphore got disposed before Wait() call");
            return false;
        }

        try
        {
            (WebSocket, ws) = (ws, WebSocket);
            return true;
        }
        finally
        {
            SemaphoreSlim.Release();
        }
    }

    #endregion
}