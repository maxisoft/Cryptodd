using System.Buffers;
using System.Buffers.Text;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Cryptodd.Utils;

public static class Utf8JsonReaderUtils
{
    public static bool TryGetDouble(ref Utf8JsonReader reader, out double res)
    {
        // ReSharper disable once InvertIf
        if (reader.TokenType == JsonTokenType.String)
        {
            // ReSharper disable once SuggestVarOrType_Elsewhere
            ReadOnlySpan<byte> span = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;
            return TryGetDoubleFromString(span, out res);
        }

        return reader.TryGetDouble(out res);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetDoubleFromString(ReadOnlySpan<byte> span, out double res)
    {
        if (Utf8Parser.TryParse(span, out double tmp, out var bytesConsumed)
            && span.Length == bytesConsumed)
        {
            res = tmp;
            return true;
        }

        res = default;
        return false;
    }
    
    public static bool TryGetInt64(ref Utf8JsonReader reader, out long res)
    {
        // ReSharper disable once InvertIf
        if (reader.TokenType == JsonTokenType.String)
        {
            // ReSharper disable once SuggestVarOrType_Elsewhere
            ReadOnlySpan<byte> span = reader.HasValueSequence ? reader.ValueSequence.ToArray() : reader.ValueSpan;
            return TryGetInt64FromString(span, out res);
        }

        return reader.TryGetInt64(out res);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetInt64FromString(ReadOnlySpan<byte> span, out long res)
    {
        if (Utf8Parser.TryParse(span, out long tmp, out var bytesConsumed)
            && span.Length == bytesConsumed)
        {
            res = tmp;
            return true;
        }

        res = default;
        return false;
    }
}