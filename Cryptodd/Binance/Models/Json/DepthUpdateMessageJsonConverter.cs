using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using Cryptodd.Ftx.Models.Json;
using Cryptodd.Json;
using Maxisoft.Utils.Collections.Lists.Specialized;

namespace Cryptodd.Binance.Models.Json;

public class DepthUpdateMessageJsonConverter : JsonConverter<DepthUpdateMessage>
{
    private static readonly PooledListConverter<BinancePriceQuantityEntry<double>>? PooledListConverter =
        new() { DefaultCapacity = 64 };

    internal static readonly StringPool StringPool = new StringPool(8 << 10);

    public override DepthUpdateMessage Read(ref Utf8JsonReader reader, Type typeToConvert,
        JsonSerializerOptions options)
    {
        string e = "", s = "";
        // ReSharper disable InconsistentNaming
        long E = 0, U = 0, u = 0;
        // ReSharper restore InconsistentNaming
        PooledList<BinancePriceQuantityEntry<double>> b = new(), a = new();
        Span<char> buffer = stackalloc char[2];

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
                    var propertyBytes = reader.ValueSpan;
                    var status = Utf8.ToUtf16(propertyBytes, buffer, out var bytesRead, out var charsWritten);
                    if (status != OperationStatus.Done)
                    {
                        throw new JsonException($"unable to read property value status: {status}", null, null,
                            reader.Position.GetInteger());
                    }

                    if (charsWritten != 1)
                    {
                        throw new JsonException($"unable to read property value charsWritten: {charsWritten}", null,
                            null, reader.Position.GetInteger());
                    }

                    var property = buffer[0];
                    if (!reader.Read())
                    {
                        throw new JsonException("unable to read property value", null, null,
                            reader.Position.GetInteger());
                    }

                    switch (property)
                    {
                        case 'e':
                            if (!StringPool.TryGetString(reader.ValueSpan, out e))
                            {
                                e = reader.GetString() ?? throw new JsonException(
                                    $"unable to read property value {property}", null, null,
                                    reader.Position.GetInteger());
                            }

                            break;
                        case 'E':
                            if (!reader.TryGetInt64(out E))
                            {
                                throw new JsonException($"unable to read property value {property}", null, null,
                                    reader.Position.GetInteger());
                            }

                            break;
                        case 's':
                            if (!StringPool.TryGetString(reader.ValueSpan, out s))
                            {
                                s = reader.GetString() ?? throw new JsonException(
                                    $"unable to read property value {property}", null, null,
                                    reader.Position.GetInteger());
                            }
                            break;
                        case 'U':
                            if (!reader.TryGetInt64(out U))
                            {
                                throw new JsonException($"unable to read property value {property}", null, null,
                                    reader.Position.GetInteger());
                            }

                            break;
                        case 'u':
                            if (!reader.TryGetInt64(out u))
                            {
                                throw new JsonException($"unable to read property value {property}", null, null,
                                    reader.Position.GetInteger());
                                ;
                            }

                            break;
                        case 'b':
                            b = PooledListConverter?.Read(ref reader, typeToConvert, options) ?? b;
                            break;
                        case 'a':
                            a = PooledListConverter?.Read(ref reader, typeToConvert, options) ?? a;
                            break;
                        default:
                            throw new JsonException($"unknown property \"{property}\"", null, null,
                                reader.Position.GetInteger());
                    }

                    break;
                default:
                    throw new JsonException(
                        $"unexpected token {reader.TokenType} expected {JsonTokenType.PropertyName}", null, null,
                        reader.Position.GetInteger());
            }

            if (!reader.Read())
            {
                throw new JsonException("unexpected end of stream", null, null, reader.Position.GetInteger());
            }
        }

        return new DepthUpdateMessage(e: e, E: E, s: s, U: U, u: u, b: b, a: a);
    }

    public override void Write(Utf8JsonWriter writer, DepthUpdateMessage value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}