using System.Net.WebSockets;
using Cryptodd.IoC;
using Lamar;

namespace Cryptodd.Bitfinex.WebSockets;

[Singleton]
public class BitfinexPublicWebSocket429Handler : IService
{
    private DateTimeOffset _lastOccurenceDate = DateTimeOffset.UnixEpoch;
    private static readonly TimeSpan BlacklistDelay = TimeSpan.FromMinutes(1);
    private int _backOffCounter = 0;

    public bool ShouldThrottle => _backOffCounter > 0 &&
        (DateTimeOffset.Now - _lastOccurenceDate).Duration() < _backOffCounter * BlacklistDelay;

    public BitfinexPublicWebSocket429Handler() { }

    public bool Is429Exception<TException>(in TException exception) where TException : Exception =>
        exception is WebSocketException wsException && wsException.Message.Contains("429"); // poor check

    public void SignalWorking()
    {
        _backOffCounter = 0;
    }

    public bool HandleConnectException<TException>(in TException exception) where TException : Exception
    {
        if (!Is429Exception(in exception))
        {
            return false;
        }

        var now = DateTimeOffset.Now;
        var duration = (now - _lastOccurenceDate).Duration();
        if (duration > Math.Max(_backOffCounter, 1) * BlacklistDelay)
        {
            _backOffCounter = Math.Max(_backOffCounter, 1);
            _lastOccurenceDate = now;
            if (duration > (_backOffCounter + 1) * BlacklistDelay)
            {
                Interlocked.Increment(ref _backOffCounter);
            }
        }

        return true;
    }
}