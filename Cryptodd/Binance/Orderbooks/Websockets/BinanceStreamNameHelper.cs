using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Cryptodd.Binance.Orderbooks.Websockets;

public static partial class BinanceStreamNameHelper
{
    [GeneratedRegex(@"@depth(?>@\d+m?s)?$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex DepthRegex();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDepth(ReadOnlySpan<char> stream) => DepthRegex().IsMatch(stream);
}