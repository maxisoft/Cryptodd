namespace Cryptodd.Okx.Websockets.Subscriptions;

public enum OkxSubscriptionSate : sbyte
{
    None = 0,
    Pending = 1,
    Subscribed = 1 << 1,
    PendingUnsubscribe = 1 << 2
}