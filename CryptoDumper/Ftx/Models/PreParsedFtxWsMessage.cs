using CryptoDumper.Ftx.Models.Json;

namespace CryptoDumper.Ftx.Models;

public readonly record struct PreParsedFtxWsMessage(string Type, string Channel, string Market,
    string Code, string Msg,
    long? Checksum,
    double? Grouping, DateTimeOffset? Time)
{
    public static bool TryParse(ReadOnlySpan<byte> bytes, out PreParsedFtxWsMessage result) =>
        PartialPreParsedFtxWsMessageParser.TryParse(bytes, out result);
}