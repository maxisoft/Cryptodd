using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Cryptodd.Binance.Orderbooks.Websockets;

public static partial class BinanceStreamNameHelper
{
    [GeneratedRegex(@"^@depth(?>(?>@[0-9]+?m?s)?)$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex DepthRegex();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDepth(ReadOnlySpan<char> stream)
    {
        var start = stream.IndexOf("@depth");
        return start > 0 && DepthRegex().IsMatch(stream[start..]);
    }
}