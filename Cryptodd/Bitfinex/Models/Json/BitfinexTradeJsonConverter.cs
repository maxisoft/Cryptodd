using System.Text.Json;
using System.Text.Json.Serialization;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Bitfinex.Models.Json;

public class BitfinexTradeConverter : JsonConverter<BitfinexTrade>
{
    public override BitfinexTrade Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        long id, mts;
        double amount, price;

        if (reader.TokenType == JsonTokenType.StartArray && reader.Read())
        {
            if (reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException($"unable to read {nameof(id)} as int64", null, null, reader.Position.GetInteger());
            }

            id = reader.GetInt64();
        }
        else
        {
            throw new JsonException("expecting a non empty JsonArray", null, null, reader.Position.GetInteger());
        }

        if (!reader.Read() || !reader.TryGetInt64(out mts))
        {
            throw new JsonException($"unable to read {nameof(mts)}", null, null, reader.Position.GetInteger());
        }

        if (!reader.Read() || !reader.TryGetDouble(out amount))
        {
            throw new JsonException($"unable to read {nameof(amount)}", null, null,
                reader.Position.GetInteger());
        }

        if (!reader.Read())
        {
            throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
        }

        if (!reader.TryGetDouble(out price))
        {
            throw new JsonException($"unable to read {nameof(price)}", null, null, reader.Position.GetInteger());
        }

        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("EndArray expected", null, null,
                reader.Position.GetInteger());
        }

        return new BitfinexTrade(id, mts, amount, price);
    }

    public override void Write(Utf8JsonWriter writer, BitfinexTrade value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}