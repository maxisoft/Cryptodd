namespace Cryptodd.Okx.Limiters;

public interface IOkxLimiterRegistry
{
    OkxLimiter WebsocketConnectionLimiter { get; }
    OkxLimiter WebsocketSubscriptionLimiter { get; }

    protected sealed class WebsocketConnectionLimiterImpl : OkxLimiter
    {
        private WebsocketConnectionLimiterImpl(TimeSpan? period) : base(period ?? TimeSpan.FromSeconds(1.1))
        {
            MaxLimit = 1;
        }

        public WebsocketConnectionLimiterImpl() : this(null) { }
    }

    protected sealed class WebsocketSubscriptionLimiterImpl : OkxLimiter
    {
        private WebsocketSubscriptionLimiterImpl(TimeSpan? period) : base(period ?? TimeSpan.FromHours(1.001))
        {
            MaxLimit = 240;
        }

        public WebsocketSubscriptionLimiterImpl() : this(null) { }
    }
}