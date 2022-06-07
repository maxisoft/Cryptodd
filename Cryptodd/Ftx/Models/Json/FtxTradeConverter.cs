using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cryptodd.Ftx.Models.Json;

public class FtxTradeConverter : JsonConverter<FtxTrade>
{
    public static readonly byte[] BuyBytes = Encoding.UTF8.GetBytes("buy");
    public static readonly byte[] SellBytes = Encoding.UTF8.GetBytes("sell");
    
    public override FtxTrade Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        long id = -1;
        double price = -1;
        double size = -1;
        var flag = FtxTradeFlag.None;
        var time = DateTimeOffset.UnixEpoch;

        if (reader.TokenType != JsonTokenType.StartObject || !reader.Read())
        {
            throw new JsonException("expecting a non empty StartArray", null, null, reader.Position.GetInteger());
        }

        while (reader.TokenType != JsonTokenType.EndObject)
        {
            // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                    var property = reader.GetString();
                    if (!reader.Read())
                    {
                        throw new JsonException("unable to read property value", null, null, reader.Position.GetInteger());
                    }
                    switch (property)
                    {
                        case "id":
                            id = reader.GetInt64();
                            break;
                        case "liquidation":
                            if (reader.GetBoolean())
                            {
                                flag |= FtxTradeFlag.Liquidation;
                            }
                            break;
                        case "price":
                            price = reader.GetDouble();
                            break;
                        case "side":
                            if (reader.ValueSpan.SequenceEqual(BuyBytes))
                            {
                                flag |= FtxTradeFlag.Buy;
                            }
                            else if (reader.ValueSpan.SequenceEqual(SellBytes))
                            {
                                flag |= FtxTradeFlag.Sell;
                            }
                            else
                            {
                                throw new JsonException($"unknown value \"{reader.GetString()}\" for side", null, null, reader.Position.GetInteger());
                            }
                            break;
                        case "size":
                            size = reader.GetDouble();
                            break;
                        case "time":
                            time = reader.GetDateTimeOffset();
                            break;
                        default:
                            throw new JsonException($"unknown property \"{property}\"", null, null, reader.Position.GetInteger());
                    }
                    break;
                default:
                    throw new JsonException($"unexpected token {reader.TokenType} expected {JsonTokenType.PropertyName}", null, null, reader.Position.GetInteger());
            }

            if (!reader.Read())
            {
                throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
            }
        }

        return new FtxTrade(id, price, size, flag, time);
    }

    public override void Write(Utf8JsonWriter writer, FtxTrade value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}