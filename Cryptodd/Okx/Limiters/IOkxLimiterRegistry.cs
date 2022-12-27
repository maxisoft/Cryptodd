using Cryptodd.Utils;

namespace Cryptodd.Okx.Limiters;

public interface IOkxLimiterRegistry
{
    OkxLimiter WebsocketConnectionLimiter { get; }
    OkxLimiter WebsocketSubscriptionLimiter { get; }

    ReferenceCounterDisposable<OkxLimiter> CreateNewWebsocketSubscriptionLimiter();

    public OkxLimiter GetHttpSubscriptionLimiter<TLimiter>(string name, string configName) where TLimiter : OkxLimiter, new();

    protected sealed class WebsocketConnectionLimiterImpl : OkxLimiter
    {
        private WebsocketConnectionLimiterImpl(TimeSpan period) : base(period, 1) { }

        public WebsocketConnectionLimiterImpl() : this(TimeSpan.FromSeconds(1.1)) { }
    }

    protected sealed class WebsocketSubscriptionLimiterImpl : OkxLimiter
    {
        private WebsocketSubscriptionLimiterImpl(TimeSpan period) : base(period, 240) { }

        public WebsocketSubscriptionLimiterImpl() : this(TimeSpan.FromHours(1.001)) { }
    }
}