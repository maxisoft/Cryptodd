using System.Buffers;
using System.Buffers.Text;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cryptodd.Ftx.Models;

namespace Cryptodd.Binance.Models.Json;

public class BinancePriceQuantityEntryConverter : JsonConverter<BinancePriceQuantityEntry<double>>
{

    private static bool TryGetDouble(ref Utf8JsonReader reader, out double res)
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
    private static bool TryGetDoubleFromString(ReadOnlySpan<byte> span, out double res)
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
    
    public override BinancePriceQuantityEntry<double> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        double price, quantity;

        if (reader.TokenType == JsonTokenType.StartArray && reader.Read())
        {
            if (!TryGetDouble(ref reader, out price))
            {
                throw new JsonException("unable to read price as double", null, null, reader.Position.GetInteger());
            }
        }
        else
        {
            throw new JsonException("expecting a non empty JsonArray", null, null, reader.Position.GetInteger());
        }

        if (reader.Read())
        {
            if (!TryGetDouble(ref reader, out quantity))
            {
                throw new JsonException("unable to read quantity as double", null, null, reader.Position.GetInteger());
            }
        }
        else
        {
            throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
        }

        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("JsonArray expected to have only 2 elements", null, null,
                reader.Position.GetInteger());
        }

        return new BinancePriceQuantityEntry<double>(price, quantity);
    }

    public override void Write(Utf8JsonWriter writer, BinancePriceQuantityEntry<double> value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, new[] { value.Price, value.Quantity });
    }
}