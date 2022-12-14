using System.Text.Json;
using Cryptodd.Binance.Models;
using Cryptodd.Json;

namespace Cryptodd.Binance.Json;

internal static class PreParsedCombinedStreamEventParser
{
    internal static readonly StringPool StringPool = new StringPool(16 << 10);

    private static readonly JsonReaderOptions Options = new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    public static bool TryParse(ReadOnlySpan<byte> bytes, out PreParsedCombinedStreamEvent result)
    {
        var stream = "";

        var done = false;

        #region ConsumeJson

        // ReSharper disable once VariableHidesOuterVariable
        void ConsumeJson(ReadOnlySpan<byte> bytes)
        {
            Span<char> buffer = stackalloc char[16];
            var reader = new Utf8JsonReader(bytes, Options);

            var startObjectCounter = 0;

            while (reader.Read() && !done)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.StartObject:
                        startObjectCounter++;
                        if (startObjectCounter > 1)
                        {
                            reader.Skip();
                        }

                        break;
                    case JsonTokenType.EndObject:
                        startObjectCounter--;
                        break;
                    case JsonTokenType.StartArray:
                        if (startObjectCounter <= 0)
                        {
                            throw new JsonException($"Unexpected {reader.TokenType}", string.Empty, 0,
                                reader.BytesConsumed);
                        }

                        reader.Skip();
                        break;
                    case JsonTokenType.PropertyName:
                        if (startObjectCounter <= 0)
                        {
                            throw new JsonException($"Unexpected {reader.TokenType}", string.Empty, 0,
                                reader.BytesConsumed);
                        }

                        if (startObjectCounter == 1)
                        {
                            var propertyBytes = reader.ValueSpan;
                            if (!reader.Read())
                            {
                                return;
                            }

                            if (reader.TokenType == JsonTokenType.String &&
                                propertyBytes.SequenceEqual("stream"u8))
                            {
                                if (!StringPool.TryGetString(reader.ValueSpan, out stream))
                                {
                                    stream = reader.GetString() ?? throw new JsonException(
                                        "unable to read stream string", string.Empty, 0,
                                        reader.BytesConsumed);
                                }

                                done = true;

                                return;
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }

                        break;
                }
            }

            done |= reader.BytesConsumed > 0 && !string.IsNullOrWhiteSpace(stream);
        }

        #endregion

        try
        {
            ConsumeJson(bytes);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException)
        {
            done = false;
        }


        result = new PreParsedCombinedStreamEvent(stream);
        return done;
    }
}