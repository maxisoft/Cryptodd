using Cryptodd.Binance.Http.RateLimiter;
using Cryptodd.IoC;

namespace Cryptodd.BinanceFutures.Http.RateLimiter;

public class EmptyBinanceFuturesRateLimiter : EmptyBinanceRateLimiter, INoAutoRegister,
    IInternalBinanceFuturesRateLimiter { }