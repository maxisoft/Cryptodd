using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Cryptodd.IoC;
using Lamar;

namespace Cryptodd.Pairs;

[Singleton]
public static class PairHasher
{
    internal const int Sha256ByteCount = 256 / sizeof(byte) / 8;
    internal const ulong Salt = 18446744073709551557ul;
    private static readonly MemoryPool<byte> MemoryPool = MemoryPool<byte>.Shared;


    public static long Hash(string pair)
    {
        if (string.IsNullOrEmpty(pair))
        {
            return 0;
        }

        Span<byte> encoded = stackalloc byte[32];
        Span<byte> destination = stackalloc byte[Sha256ByteCount];
        var byteCount = Encoding.UTF8.GetByteCount(pair);
        var res = byteCount >= encoded.Length
            ? HashWithAlloc(pair, byteCount, destination)
            : DoHash(pair, byteCount, encoded, destination);
        return res;
    }

    private static long HashWithAlloc(string pair, int byteCount, Span<byte> result)
    {
        using var encodedMemory = MemoryPool.Rent(byteCount);

        return DoHash(pair, byteCount, encodedMemory.Memory.Span, result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static long DoHash(string pair, int byteCount, Span<byte> buffer, Span<byte> destination)
    {
        var encodedBytes = Encoding.UTF8.GetBytes(pair, buffer);

        if (encodedBytes != byteCount)
        {
            throw new ArgumentException("invalid byte count", nameof(byteCount));
        }

        if (!SHA256.TryHashData(buffer[..encodedBytes], destination, out var bytesWritten) ||
            bytesWritten != Sha256ByteCount)
        {
            throw new ArgumentException("invalid byte count", nameof(pair));
        }

        Debug.Assert(destination.Length == bytesWritten);

        var asLong = MemoryMarshal.Cast<byte, long>(destination);
        var res = Salt;

        unchecked
        {
            foreach (var l in asLong)
            {
                res ^= (ulong)IPAddress.HostToNetworkOrder(l);
            }

            res &= ~(1ul << 63); // remove sign bit 
        }

        Debug.Assert((long)res >= 0);

        return (long)res;
    }
}