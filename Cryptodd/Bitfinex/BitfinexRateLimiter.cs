using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Cryptodd.Bitfinex;

public sealed class BitfinexRateLimiter
{
    public const int OneMinuteAsMilli = 60_000;
    public int MaxRequestPerMinutes { get; init; } = 10;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private int _requestCounter;
    public int RequestCounter => _requestCounter;

    public TimeSpan ComputeWaitTime()
    {
        if (_stopwatch.ElapsedMilliseconds > OneMinuteAsMilli)
        {
            lock (_stopwatch)
            {
                if (_stopwatch.ElapsedMilliseconds > OneMinuteAsMilli)
                {
                    _requestCounter = 0;
                    _stopwatch.Restart();
                }
            }
        }

        if (RequestCounter >= MaxRequestPerMinutes)
        {
            return TimeSpan.FromMilliseconds(Math.Max(OneMinuteAsMilli - _stopwatch.ElapsedMilliseconds, 0));
        }

        return TimeSpan.Zero;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RegisterApiCall()
    {
        Interlocked.Increment(ref _requestCounter);
    }

    public RateLimiterHelper Helper() => new RateLimiterHelper(this);

    public struct RateLimiterHelper : IDisposable
    {
        private readonly BitfinexRateLimiter _rateLimiter;
        public bool AutoRegisterApiCall { get; set; } = true;
        
        internal RateLimiterHelper(BitfinexRateLimiter rateLimiter)
        {
            _rateLimiter = rateLimiter;
        }


        public async ValueTask Wait(CancellationToken cancellationToken = default)
        {
            TimeSpan timeSpan;
            do
            {
                timeSpan = _rateLimiter.ComputeWaitTime();
                await Task.Delay(timeSpan, cancellationToken);
            } while (timeSpan > TimeSpan.Zero && !cancellationToken.IsCancellationRequested);
        }
        
        public void Dispose()
        {
            if (AutoRegisterApiCall)
            {
                _rateLimiter.RegisterApiCall();
            }
        }
    }
}