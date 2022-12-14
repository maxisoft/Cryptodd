using System.Runtime.CompilerServices;

namespace Cryptodd.Binance.Orderbooks;

public interface IOrderBookEntry
{
    double Price { get; init; }

    DateTimeOffset Time { get; set; }

    /// <summary>
    /// Internal <a href="https://binance-docs.github.io/apidocs/spot/en/#how-to-manage-a-local-order-book-correctly">Binance Update Identifier</a>.
    /// </summary>
    long UpdateId { get; set; }

    double Quantity { get; set; }
    int ChangeCounter { get; set; }


    void ResetStatistics();
    void Update(double qty, DateTimeOffset time, long updateId);

    #region Default Generic Implementations

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void DoResetStatistics<T>(ref T entry) where T : IOrderBookEntry
    {
        entry.ChangeCounter = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static void DoUpdate<T>(ref T entry, double qty, DateTimeOffset time, long updateId)
        where T : IOrderBookEntry
    {
        if (updateId < entry.UpdateId)
        {
            throw new ArgumentOutOfRangeException(nameof(updateId), updateId,
                "trying to update entry to an older version");
        }

        entry.UpdateId = updateId;
        entry.Time = time;
        entry.Quantity = qty;
        entry.ChangeCounter += 1;
    }

    #endregion
}