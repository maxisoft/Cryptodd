using System.Text.Json;
using System.Text.Unicode;

namespace Cryptodd.Binance.Models;

internal static class PreParsedCombinedStreamEventParser
{
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
                            Utf8.ToUtf16(propertyBytes, buffer, out var bytesRead, out var propertyLength);

                            var property = buffer[..propertyLength].Trim();
                            if (!reader.Read())
                            {
                                return;
                            }

                            if (bytesRead == propertyBytes.Length && reader.TokenType == JsonTokenType.String &&
                                property.SequenceEqual("stream"))
                            {
                                done = true;
                                stream = reader.GetString();
                                
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