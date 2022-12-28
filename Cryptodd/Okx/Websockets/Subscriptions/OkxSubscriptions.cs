using System.Collections;
using System.Collections.Concurrent;

namespace Cryptodd.Okx.Websockets.Subscriptions;

public class OkxSubscriptions: IReadOnlyCollection<OkxSubscription>
{
    protected readonly ConcurrentDictionary<OkxSubscription, DateTimeOffset> Subscriptions = new();
    protected readonly ConcurrentDictionary<OkxSubscription, DateTimeOffset> PendingSubscriptions = new();
    protected readonly ConcurrentDictionary<OkxSubscription, DateTimeOffset> PendingUnsubscriptions = new();

    public int Count => Subscriptions.Count + PendingSubscriptions.Count + PendingUnsubscriptions.Count;

    public bool PendingSubscription<T>(in T subscription) where T : OkxSubscription =>
        PendingSubscription(in subscription, DateTimeOffset.Now);

    public int SubscriptionsCount => Subscriptions.Count;
    public int PendingSubscriptionsCount => PendingSubscriptions.Count;
    public int PendingUnsubscriptionsCount => PendingUnsubscriptions.Count;

    public bool PendingSubscription<T>(in T subscription, DateTimeOffset dateTime) where T : OkxSubscription
    {
        if (Subscriptions.ContainsKey(subscription))
        {
            throw new ArgumentException("subscription already in confirmed subscription list");
        }

        if (PendingSubscriptions.TryAdd(subscription, dateTime != default ? dateTime : DateTimeOffset.Now))
        {
            PendingUnsubscriptions.TryRemove(subscription, out _);
            return true;
        }

        return false;
    }

    public bool ConfirmSubscription<T>(in T subscription, bool checkWasPending = true) where T : OkxSubscription
    {
        DateTimeOffset datetime = default;
        if (!PendingSubscriptions.TryRemove(subscription, out datetime) && checkWasPending)
        {
            throw new ArgumentException("subscription not in pending subscription list");
        }

        return Subscriptions.TryAdd(subscription, datetime != default ? datetime : DateTimeOffset.Now);
    }

    public bool Unsubscribe<T>(in T subscription, bool checkWasSubscribed = true) where T : OkxSubscription
    {
        DateTimeOffset datetime = default;
        if (!Subscriptions.TryRemove(subscription, out datetime) && checkWasSubscribed)
        {
            throw new ArgumentException("subscription not in subscription list");
        }

        return PendingUnsubscriptions.TryAdd(subscription, datetime != default ? datetime : DateTimeOffset.Now);
    }

    public int ForceRemove<T>(in T subscription) where T : OkxSubscription
    {
        var c = 0;
        c += PendingSubscriptions.TryRemove(subscription, out _) ? 1 : 0;
        c += Subscriptions.TryRemove(subscription, out _) ? 1 : 0;
        c += PendingUnsubscriptions.TryRemove(subscription, out _) ? 1 : 0;
        return c;
    }

    public OkxSubscriptionSate GetState<T>(in T subscription) where T : OkxSubscription
    {
        if (PendingSubscriptions.ContainsKey(subscription))
        {
            return OkxSubscriptionSate.Pending;
        }

        if (Subscriptions.ContainsKey(subscription))
        {
            return OkxSubscriptionSate.Subscribed;
        }

        // ReSharper disable once ConvertIfStatementToReturnStatement
        if (PendingUnsubscriptions.ContainsKey(subscription))
        {
            return OkxSubscriptionSate.PendingUnsubscribe;
        }

        return OkxSubscriptionSate.None;
    }

    public bool Contains<T>(in T subscription) where T : OkxSubscription => GetState(in subscription) != OkxSubscriptionSate.None;

    public IEnumerator<OkxSubscription> GetEnumerator()
    {
        foreach (var subscription in PendingSubscriptions.Keys)
        {
            yield return subscription;
        }

        foreach (var subscription in Subscriptions.Keys)
        {
            yield return subscription;
        }
        
        foreach (var subscription in PendingUnsubscriptions.Keys)
        {
            yield return subscription;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}