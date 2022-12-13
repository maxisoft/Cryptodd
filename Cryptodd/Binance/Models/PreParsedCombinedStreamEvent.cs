using System.Buffers.Text;
using System.Globalization;
using System.Text;
using Cryptodd.Ftx.Models;

namespace Cryptodd.Binance.Models;

public readonly record struct PreParsedCombinedStreamEvent(string Stream)
{
    public static bool TryParse(ReadOnlySpan<byte> bytes, out PreParsedCombinedStreamEvent result) =>
        PreParsedCombinedStreamEventParser.TryParse(bytes, out result);
}