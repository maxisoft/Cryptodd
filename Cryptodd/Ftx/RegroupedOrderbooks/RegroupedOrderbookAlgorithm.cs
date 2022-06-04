using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Cryptodd.Ftx.Models;
using Cryptodd.Utils;
using Maxisoft.Utils.Empties;

namespace Cryptodd.Ftx.RegroupedOrderbooks;

public static class RegroupedOrderbookAlgorithm
{
    public const int DefaultSize = 25;
    internal static MemoryPool<double> DoubleMemoryPool { get; set; } = MemoryPool<double>.Shared;

    internal static Span<short> GeometricSpaceIndex<TBase>(int start, int end, int step, float rounding,
        Span<short> result, int srcStep = 0, bool reverse = true, TBase logBase = default)
        where TBase : struct, IBaseLog
    {
        var res = result[..step];

        var startLog = logBase.Log(start);
        var endLog = logBase.Log(end);

        var diffLog = endLog - startLog;
        if (srcStep == 0)
        {
            srcStep = step;
        }

        var step1 = srcStep - 1;

        for (var i = 0; i < step; i++)
        {
            var x = logBase.Exp(startLog + i * diffLog / step1);
            var index = i;
            if (reverse)
            {
                x = end - (x - start);
                index = step - 1 - i;
            }


            if (rounding != 0)
                // ReSharper disable once CompareOfFloatsByEqualityOperator
            {
                x = rounding == 1 ? MathF.Round(x) : PriceUtils.RoundF(x, rounding);
            }

            res[index] = (short)x;
        }

        checked
        {
            res[0] = (short)start;
            res[^1] = (short)end;
        }

        return res;
    }

    internal static void FixGeomSpaceIndex(Span<short> buffer)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        var prev = buffer[^1];

        for (var i = buffer.Length - 2; i > -1; i--)
        {
            var current = buffer[i];
            if (current >= prev)
            {
                current = (short)Math.Max(prev - 1, 0);
                buffer[i] = current;
            }

            prev = current;
        }

        prev = buffer[0];
        for (var i = 1; i < buffer.Length; i++)
        {
            var current = buffer[i];
            if (current <= prev)
            {
                current = (short)Math.Min(prev + 1, buffer.Length - 1);
                buffer[i] = current;
            }

            prev = current;
        }
    }

    internal static void FastRegression(Span<double> prices, int targetSize, bool reverse, Span<double> result)
    {
        Debug.Assert(targetSize > prices.Length);
        if (reverse)
        {
            prices.Reverse();
        }

        try
        {
            var prev = result[0] = prices[0];
            double acc = 0;
            var divisor = 0;

            for (var i = 1; i < prices.Length; i++)
            {
                var price = prices[i];
                if (prev != 0)
                {
                    acc += price / prev - 1;
                    divisor++;
                }

                result[i] = price;
                prev = price;
            }

            if (divisor > 0)
            {
                acc /= divisor;
            }

            for (var i = prices.Length; i < targetSize; i++)
            {
                result[i] = prev = prev * (1 + acc);
            }
        }
        finally
        {
            if (reverse)
            {
                prices.Reverse();
            }
        }

        if (reverse)
        {
            result.Reverse();
        }
    }

    internal static (IMemoryOwner<double> prices, IMemoryOwner<double> sizes) Pad(Span<double> obPrice,
        Span<double> obSize, bool reverse = false, int n = DefaultSize)
    {
        var prices = DoubleMemoryPool.Rent(n);
        try
        {
            var sizes = DoubleMemoryPool.Rent(n);
            FastRegression(obPrice, n, reverse, prices.Memory.Span[..n]);
            var sizesSpan = sizes.Memory.Span[..n];
            sizesSpan.Clear();

            obSize.CopyTo(reverse ? sizesSpan[^obSize.Length..] : sizesSpan[..obSize.Length]);

            return (prices, sizes);
        }
        catch (Exception)
        {
            prices.Dispose();
            throw;
        }
    }


    public static RegroupedOrderbook Create(GroupedOrderbook orderbook, int n = DefaultSize, bool legacy = false)
    {
        Debug.Assert(Marshal.SizeOf<PriceSizePair>() == 2 * sizeof(double));
        using var bidMemory = DoubleMemoryPool.Rent(orderbook.Bids.Count * 2);
        using var askMemory = DoubleMemoryPool.Rent(orderbook.Asks.Count * 2);

        var bids = MemoryMarshal.Cast<double, PriceSizePair>(bidMemory.Memory.Span)[..orderbook.Bids.Count];
        var asks = MemoryMarshal.Cast<double, PriceSizePair>(askMemory.Memory.Span)[..orderbook.Asks.Count];

        ((Span<PriceSizePair>)orderbook.Asks).CopyTo(asks);
        ((Span<PriceSizePair>)orderbook.Bids).CopyTo(bids);
        bids.Sort();
        asks.Sort();

        if (!legacy)
        {
            static Span<PriceSizePair> Trim(Span<PriceSizePair> pairs)
            {
                while (!pairs.IsEmpty)
                {
                    if (pairs[^1].Size <= 0)
                    {
                        pairs = pairs[..^1];
                    }
                    else if (pairs[0].Size <= 0)
                    {
                        pairs = pairs[1..];
                    }
                    else if (pairs[0].Price <= 0)
                    {
                        pairs = pairs[1..];
                    }
                    else
                    {
                        break;
                    }
                }

                return pairs;
            }


            asks = Trim(asks);
            bids = Trim(bids);

            // need to not overflow short.MaxValue
            if (bids.Length >= short.MaxValue)
            {
                bids = bids[..(short.MaxValue - 1)];
            }

            if (asks.Length >= short.MaxValue)
            {
                asks = asks[^(short.MaxValue - 1)..];
            }
        }

        var res = new RegroupedOrderbook();

        var compressedBids = CompressBids(bids, n);
        try
        {
            var priceBids = compressedBids.prices.Memory.Span[..n];
            var sizeBids = compressedBids.sizes.Memory.Span[..n];
            var resultingBids = res.Bids = new PriceSizePair[n];
            for (var i = 0; i < n; i++)
            {
                resultingBids[i] = new PriceSizePair(priceBids[i], sizeBids[i]);
            }
        }
        finally
        {
            compressedBids.prices.Dispose();
            compressedBids.sizes.Dispose();
        }

        var compressedAsks = CompressAsks(asks, n);
        try
        {
            var priceAsks = compressedAsks.prices.Memory.Span[..n];
            var sizeAsks = compressedAsks.sizes.Memory.Span[..n];
            var resultingAsks = res.Asks = new PriceSizePair[n];
            for (var i = 0; i < n; i++)
            {
                resultingAsks[i] = new PriceSizePair(priceAsks[i], sizeAsks[i]);
            }
        }
        finally
        {
            compressedAsks.prices.Dispose();
            compressedAsks.sizes.Dispose();
        }

        return res;
    }

    public static RegroupedOrderbook Create(GroupedOrderbookDetails orderbookGroupedWrapper, int n = DefaultSize,
        bool legacy = false)
    {
        var res = Create(orderbookGroupedWrapper.Data, n, legacy);
        res.Market = orderbookGroupedWrapper.Market;
        res.Time = orderbookGroupedWrapper.Time;
        return res;
    }

    internal static (IMemoryOwner<double> prices, IMemoryOwner<double> sizes) CompressBids(
        Span<PriceSizePair> priceSizePairs,
        int n = DefaultSize)
    {
        using var priceMemory = DoubleMemoryPool.Rent(priceSizePairs.Length);
        using var sizeMemory = DoubleMemoryPool.Rent(priceSizePairs.Length);

        var obPrice = priceMemory.Memory.Span[..priceSizePairs.Length];
        var obSize = sizeMemory.Memory.Span[..priceSizePairs.Length];

        for (var i = 0; i < priceSizePairs.Length; i++)
        {
            obPrice[i] = priceSizePairs[i].Price;
            obSize[i] = priceSizePairs[i].Size;
        }

        var size = priceSizePairs.Length;
        IMemoryOwner<double> prices;
        IMemoryOwner<double> sizes;
        if (size <= 1)
        {
            prices = DoubleMemoryPool.Rent(n);
            try
            {
                sizes = DoubleMemoryPool.Rent(n);
                try
                {
                    prices.Memory.Span.Fill(0);
                    sizes.Memory.Span.Fill(0);


                    obPrice.CopyTo(prices.Memory.Span[..size]);
                    obSize.CopyTo(sizes.Memory.Span[..size]);

                    if (size > 0)
                    {
                        for (var i = size; i < n; i++)
                        {
                            prices.Memory.Span[i] = Math.Max(obPrice[^1] / Math.Pow(1 + 0.0005, i), 0);
                        }
                    }

                    return (prices, sizes);
                }
                catch (Exception)
                {
                    sizes.Dispose();
                    throw;
                }
            }
            catch (Exception)
            {
                prices.Dispose();
                throw;
            }
        }

        var startPrice = obPrice[0];
        if (startPrice <= 0)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            if (size > 1)
            {
                startPrice = Math.Max(1e-6, obPrice[1] - 1e-6);
            }
            else
            {
                startPrice = 1e-6;
            }
        }

        var endPrice = obPrice[^1];
        // remove first price aggregation as it may contain an outlier size lately
        var dropFirst = obPrice.Length > n && Math.Abs(Math.Log(startPrice) - Math.Log(endPrice)) > 1;

        Span<short> buff = stackalloc short[32];

        IDisposable buffDisposable = new EmptyDisposable();
        var step = n + 1 + (dropFirst ? 1 : 0);
        if (buff.Length < step)
        {
            Debug.Assert(sizeof(double) % sizeof(short) == 0);
            var tmpBuff = DoubleMemoryPool.Rent((step + 1) / (sizeof(double) / sizeof(short)));
            buffDisposable = tmpBuff;
            buff = MemoryMarshal.Cast<double, short>(tmpBuff.Memory.Span);
        }

        using var d1 = buffDisposable;
        Debug.Assert(buff.Length >= n);

        var pricesIndex = GeometricSpaceIndex<BaseLog2>(1, obPrice.Length - 1, step, 1.0f,
            buff, reverse: true);

        startPrice = dropFirst ? obPrice[pricesIndex[1]] : 0;
        pricesIndex = pricesIndex[(1 + (dropFirst ? 1 : 0))..];
        FixGeomSpaceIndex(pricesIndex);

        (IMemoryOwner<double> prices, IMemoryOwner<double> sizes)? newPriceSize = null;
        if (pricesIndex.Length > obPrice.Length)
        {
            newPriceSize = Pad(obPrice, obSize, true, n);
            obPrice = newPriceSize.Value.prices.Memory.Span[..n];
            obSize = newPriceSize.Value.sizes.Memory.Span[..n];
        }

        using var d2 = (IDisposable?)newPriceSize?.prices ?? new EmptyDisposable();
        using var d3 = (IDisposable?)newPriceSize?.sizes ?? new EmptyDisposable();

        prices = DoubleMemoryPool.Rent(pricesIndex.Length); // TODO try except dispose
        try
        {
            sizes = DoubleMemoryPool.Rent(pricesIndex.Length);
            try
            {
                var priceSpan = prices.Memory.Span[..pricesIndex.Length];
                priceSpan.Clear();
                var sizeSpan = sizes.Memory.Span[..pricesIndex.Length];
                sizeSpan.Clear();

                for (var i = 0; i < pricesIndex.Length; i++)
                {
                    priceSpan[i] = obPrice[pricesIndex[i]];
                }

                var slice = new Range(0, BisectLeft(obPrice, startPrice));

                for (var i = 0; i < priceSpan.Length; i++)
                {
                    var price = priceSpan[i];
                    var index = BisectLeft(obPrice, price);
                    index = Math.Min(index, obPrice.Length - 1);
                    if (index + 1 < slice.End.GetOffset(obSize.Length))
                    {
                        break;
                    }
                    slice = new Range(slice.End, index + 1);
                    foreach (var s in obSize[slice])
                    {
                        sizeSpan[i] += s;
                    }
                }

                return (prices, sizes);
            }
            catch (Exception)
            {
                sizes.Dispose();
                throw;
            }
        }
        catch (Exception)
        {
            prices.Dispose();
            throw;
        }
    }

    internal static (IMemoryOwner<double> prices, IMemoryOwner<double> sizes) CompressAsks(
        Span<PriceSizePair> priceSizePairs,
        int n = DefaultSize)
    {
        using var priceMemory = DoubleMemoryPool.Rent(priceSizePairs.Length);
        using var sizeMemory = DoubleMemoryPool.Rent(priceSizePairs.Length);

        var obPrice = priceMemory.Memory.Span[..priceSizePairs.Length];
        var obSize = sizeMemory.Memory.Span[..priceSizePairs.Length];

        for (var i = 0; i < priceSizePairs.Length; i++)
        {
            obPrice[i] = priceSizePairs[i].Price;
            obSize[i] = priceSizePairs[i].Size;
        }

        var size = priceSizePairs.Length;
        IMemoryOwner<double> prices;
        IMemoryOwner<double> sizes;
        if (size <= 1)
        {
            prices = DoubleMemoryPool.Rent(n);
            try
            {
                sizes = DoubleMemoryPool.Rent(n);
                try
                {
                    prices.Memory.Span.Fill(0);
                    sizes.Memory.Span.Fill(0);


                    obPrice.CopyTo(prices.Memory.Span[..size]);
                    obSize.CopyTo(sizes.Memory.Span[..size]);

                    if (size > 0)
                    {
                        for (var i = size; i < n; i++)
                        {
                            prices.Memory.Span[i] = Math.Max(obPrice[^1] / Math.Pow(1 + 0.0005, i), 0);
                        }
                    }

                    return (prices, sizes);
                }
                catch (Exception)
                {
                    sizes.Dispose();
                    throw;
                }
            }
            catch (Exception)
            {
                prices.Dispose();
                throw;
            }
        }

        Debug.Assert(obPrice.IsEmpty || obPrice[0] > 0);
        var startPrice = obPrice[0];

        var endPrice = obPrice[^1];
        // remove first price aggregation as it may contain an outlier size lately
        var dropLast = obPrice.Length > n && Math.Abs(Math.Log(startPrice) - Math.Log(endPrice)) > 1;

        Span<short> buff = stackalloc short[32];

        IDisposable buffDisposable = new EmptyDisposable();
        var step = n + (dropLast ? 1 : 0);
        if (buff.Length < step)
        {
            Debug.Assert(sizeof(double) % sizeof(short) == 0);
            var tmpBuff = DoubleMemoryPool.Rent((step + 1) / (sizeof(double) / sizeof(short)));
            buffDisposable = tmpBuff;
            buff = MemoryMarshal.Cast<double, short>(tmpBuff.Memory.Span);
        }

        using var d1 = buffDisposable;
        Debug.Assert(buff.Length >= n);

        var pricesIndex = GeometricSpaceIndex<BaseLog2>(1, obPrice.Length, step, 1.0f,
            buff, reverse: false);

        for (var i = 0; i < pricesIndex.Length; i++)
        {
            pricesIndex[i] -= 1;
        }

        Debug.Assert(pricesIndex[0] >= 0);
        Debug.Assert(pricesIndex[0] <= pricesIndex[^1]);

        var lastPrice = dropLast ? obPrice[pricesIndex[^1]] : 0;
        if (dropLast)
        {
            pricesIndex = pricesIndex[..^1];
        }

        FixGeomSpaceIndex(pricesIndex);

        (IMemoryOwner<double> prices, IMemoryOwner<double> sizes)? newPriceSize = null;
        if (pricesIndex.Length > obPrice.Length)
        {
            newPriceSize = Pad(obPrice, obSize, false, n);
            obPrice = newPriceSize.Value.prices.Memory.Span[..n];
            obSize = newPriceSize.Value.sizes.Memory.Span[..n];
            lastPrice = obPrice[pricesIndex[^1]];
        }

        using var d2 = (IDisposable?)newPriceSize?.prices ?? new EmptyDisposable();
        using var d3 = (IDisposable?)newPriceSize?.sizes ?? new EmptyDisposable();

        prices = DoubleMemoryPool.Rent(pricesIndex.Length);
        try
        {
            sizes = DoubleMemoryPool.Rent(pricesIndex.Length);
            try
            {
                var priceSpan = prices.Memory.Span[..pricesIndex.Length];
                priceSpan.Clear();
                var sizeSpan = sizes.Memory.Span[..pricesIndex.Length];
                sizeSpan.Clear();

                for (var i = 0; i < pricesIndex.Length; i++)
                {
                    priceSpan[i] = obPrice[pricesIndex[i]];
                }

                var slice = new Range(BisectRight(obPrice, lastPrice), obPrice.Length);


                for (var i = priceSpan.Length - 1; i > -1; i--)
                {
                    var price = priceSpan[i];
                    var index = BisectRight(obPrice, price) - 1;
                    index = Math.Max(index, 0);
                    if (index > slice.Start.GetOffset(obSize.Length))
                    {
                        break;
                    }
                    slice = new Range(index, slice.Start);
                    foreach (var s in obSize[slice])
                    {
                        sizeSpan[i] += s;
                    }
                }

                return (prices, sizes);
            }
            catch (Exception)
            {
                sizes.Dispose();
                throw;
            }
        }
        catch (Exception)
        {
            prices.Dispose();
            throw;
        }
    }

    public static int BisectLeft<T>(Span<T> span, in T needle) where T : IComparable<T>
    {
        var res = span.BinarySearch(needle);
        if (res < 0)
        {
            return ~res;
        }

        Debug.Assert(res <= span.Length);
        return res;
    }

    public static int BisectRight<T>(Span<T> span, in T needle) where T : IComparable<T>
    {
        var res = span.BinarySearch(needle);
        if (res < 0)
        {
            return ~res;
        }

        Debug.Assert(res <= span.Length);
        return Math.Min(res + 1, span.Length);
    }
}