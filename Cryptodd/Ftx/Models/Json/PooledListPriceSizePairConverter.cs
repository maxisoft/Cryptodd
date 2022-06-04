using System.Text.Json;
using System.Text.Json.Serialization;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Ftx.Models.Json;

public class PooledListPriceSizePairConverter : JsonConverter<PooledList<PriceSizePair>>
{
    public override PooledList<PriceSizePair>? Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        var converter = (JsonConverter<PriceSizePair>) options.GetConverter(typeof(PriceSizePair));
        var res = new PooledList<PriceSizePair>();
        try
        {
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                while (reader.Read() && reader.TokenType == JsonTokenType.StartArray)
                {
                    var pair = converter.Read(ref reader, typeof(PriceSizePair), options);
                    res.Add(pair);
                }

                if (reader.TokenType != JsonTokenType.EndArray)
                {
                    throw new JsonException("expecting EndArray", null, null, reader.Position.GetInteger());
                }

            }
            else
            {
                throw new JsonException("expecting a JsonArray", null, null, reader.Position.GetInteger());
            }

            return res;
        }
        catch (Exception)
        {
            res.Dispose();
            throw;
        }
        
        
    }

    public override void Write(Utf8JsonWriter writer, PooledList<PriceSizePair> value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}