using System.Text.RegularExpressions;

namespace Cryptodd.Binance.Orderbooks.Websockets;

public static partial class BinanceStreamNameHelper
{
    [GeneratedRegex(@"@depth(?:@\d+m?s)?$", RegexOptions.Singleline | RegexOptions.NonBacktracking)]
    private static partial Regex DepthRegex();

    public static bool IsDepth(ReadOnlySpan<char> stream) => DepthRegex().IsMatch(stream);
}