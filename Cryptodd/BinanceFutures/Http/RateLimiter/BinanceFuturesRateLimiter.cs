using Cryptodd.Binance.Http.RateLimiter;
using Lamar;
using Maxisoft.Utils.Objects;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace Cryptodd.BinanceFutures.Http.RateLimiter;

public interface IBinanceFuturesRateLimiter : IBinanceRateLimiter
{
    public const int DefaultMaxUsableWeight = 2400;
}

public interface IInternalBinanceFuturesRateLimiter : IInternalBinanceRateLimiter, IBinanceFuturesRateLimiter { }

[Singleton]
// ReSharper disable once ClassNeverInstantiated.Global
public sealed class BinanceFuturesRateLimiter : BinanceRateLimiter, IInternalBinanceFuturesRateLimiter
{
    public BinanceFuturesRateLimiter(BinanceHttpUsedWeightCalculator weightCalculator, ILogger logger,
        IConfiguration configuration, Boxed<CancellationToken> cancellationToken) : base(weightCalculator, logger,
        configuration, cancellationToken)
    {
        ReConfigure(Options);
        configuration.GetSection("BinanceFutures:Http:RateLimiter").Bind(Options);
        MaxUsableWeight = Options.DefaultMaxUsableWeight;
        AvailableWeightMultiplier = Options.AvailableWeightMultiplier;
    }

    private static void ReConfigure(BinanceRateLimiterOptions options)
    {
        options.DefaultMaxUsableWeight = IInternalBinanceFuturesRateLimiter.DefaultMaxUsableWeight;
    }
}