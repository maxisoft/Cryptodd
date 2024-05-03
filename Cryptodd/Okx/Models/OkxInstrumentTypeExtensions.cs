using System.Runtime.CompilerServices;
using Cryptodd.Utils.FastMapFork;

namespace Cryptodd.Okx.Models

;

public static class OkxInstrumentTypeExtensions
{
    private static readonly FastMapFork<int, string> CachedToHttpString = CreateCached();

    private static FastMapFork<int, string> CreateCached()
    {
        var values = Enum.GetValues<OkxInstrumentType>();
        var res = new FastMapFork<int, string>(checked((uint)values.Length));
        foreach (var value in values)
        {
            var str = ConvertToHttpString(value);
            if (!string.IsNullOrEmpty(str))
            {
                res.TryEmplace((int)value, str);
            }
        }

        return res;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static string ToHttpString(this OkxInstrumentType instrumentType)
    {
        FastEntry<int, string> defaultValue = default;
        ref var entry = ref CachedToHttpString.GetRef((int)instrumentType, ref defaultValue);
        return !string.IsNullOrEmpty(entry.Value) ? entry.Value : ConvertToHttpStringOrThrow(instrumentType);
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string? ConvertToHttpString(OkxInstrumentType instrumentType) =>
        Enum.GetName(instrumentType)?.ToUpperInvariant();
    
    private static string ConvertToHttpStringOrThrow(OkxInstrumentType instrumentType) =>
        ConvertToHttpString(instrumentType) ??
        throw new ArgumentOutOfRangeException(nameof(instrumentType));
}