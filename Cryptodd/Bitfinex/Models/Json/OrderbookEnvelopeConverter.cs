using System.Text.Json;
using System.Text.Json.Serialization;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Bitfinex.Models.Json;

public class OrderbookEnvelopeConverter : JsonConverter<OrderbookEnvelope>
{
    public override OrderbookEnvelope Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        long channel;
        long messageId;
        long time;

        var converter =
            (JsonConverter<PooledList<PriceCountSizeTuple>>)options.GetConverter(
                typeof(PooledList<PriceCountSizeTuple>));

        if (reader.TokenType == JsonTokenType.StartArray && reader.Read())
        {
            if (!reader.TryGetInt64(out channel))
            {
                throw new JsonException("unable to read channel as int64", null, null, reader.Position.GetInteger());
            }
        }
        else
        {
            throw new JsonException("expecting a non empty JsonArray", null, null, reader.Position.GetInteger());
        }

        if (!reader.Read())
        {
            throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
        }
        var obs = converter.Read(ref reader, typeof(PooledList<PriceCountSizeTuple>), options);

        if (reader.Read())
        {
            if (!reader.TryGetInt64(out messageId))
            {
                throw new JsonException("unable to read messageId as int64", null, null, reader.Position.GetInteger());
            }
        }
        else
        {
            throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
        }

        if (reader.Read())
        {
            if (!reader.TryGetInt64(out time))
            {
                throw new JsonException("unable to read time as int64", null, null, reader.Position.GetInteger());
            }
        }
        else
        {
            throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
        }

        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
        {
            throw new JsonException("JsonArray expected to have only 4 elements", null, null,
                reader.Position.GetInteger());
        }

        return new OrderbookEnvelope(
            Channel: channel,
            MessageId: messageId,
            Time: time
        ) { Orderbook = obs! };
    }

    public override void Write(Utf8JsonWriter writer, OrderbookEnvelope value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}