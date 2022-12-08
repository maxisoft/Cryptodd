using System.Collections;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Binance.Orderbook;

public partial class InMemoryOrderbook<T>
{
    public abstract class SortedView : IReadOnlyCollection<T>, IDisposable
    {
        protected readonly InMemoryOrderbook<T> Orderbook;
        private long? _version;
        private PooledList<PriceRoundKey>? _keys;

        protected SortedView(InMemoryOrderbook<T> orderbook)
        {
            Orderbook = orderbook;
        }

        protected abstract ConcurrentDictionary<PriceRoundKey, T> Collection { get; }
        protected abstract long Version { get; }

        public bool OutOfDate => _version is null || _version != Version;

        public IEnumerator<T> GetEnumerator()
        {
            if (_keys is null)
            {
                _keys = CreateKeys();
                CheckConcurrentModification();
            }

            var coll = Collection;
            var keys = _keys;
            for (var index = 0; index < keys.Count; index++)
            {
                var key = keys[index];
                if (coll.TryGetValue(key, out var value))
                {
                    yield return value;
                }
                else
                {
                    yield return new T { Price = key.RoundedPrice, Time = DateTimeOffset.Now };
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public int Count
        {
            get
            {
                // ReSharper disable once InvertIf
                if (_keys is null)
                {
                    _keys = CreateKeys();
                    CheckConcurrentModification();
                }


                return _keys.Count;
            }
        }

        private PooledList<PriceRoundKey> CreateKeys()
        {
            var collection = Collection;
            PooledList<PriceRoundKey> res = new(collection.Count);
            try
            {
                lock (collection)
                {
                    foreach (var (key, value) in collection)
                    {
                        if (value.Quantity > 0)
                        {
                            res.Add(key);
                        }
                    }
                }

                res.AsSpan().Sort(PriceRoundKeyComparison);
            }
            catch
            {
                res.Dispose();
                throw;
            }

            return res;
        }

        public void EnforceKeysEnumeration()
        {
            CreateKeys();
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static int PriceRoundKeyComparison(PriceRoundKey left, PriceRoundKey right) =>
            left.CompareTo(right);

        public void CheckConcurrentModification()
        {
            _version ??= Version;

            if (_version != Version)
            {
                throw new InvalidOperationException("Concurrent modification detected");
            }
        }

        private void ReleaseUnmanagedResources()
        {
            _keys?.Dispose();
        }

        private void Dispose(bool disposing)
        {
            ReleaseUnmanagedResources();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~SortedView()
        {
            Dispose(false);
        }
    }

    private sealed class BidSortedView : SortedView
    {
        public BidSortedView(InMemoryOrderbook<T> orderbook) : base(orderbook) { }
        protected override ConcurrentDictionary<PriceRoundKey, T> Collection => Orderbook._bids;
        protected override long Version => Orderbook._bidsVersion;
    }

    private sealed class AskSortedView : SortedView
    {
        public AskSortedView(InMemoryOrderbook<T> orderbook) : base(orderbook) { }
        protected override ConcurrentDictionary<PriceRoundKey, T> Collection => Orderbook._asks;
        protected override long Version => Orderbook._asksVersion;
    }
}