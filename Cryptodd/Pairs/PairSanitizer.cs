using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using BitFaster.Caching.Lfu;
using Maxisoft.Utils.Logic;
using Microsoft.Extensions.Caching.Memory;

namespace Cryptodd.Pairs;

public static class PairSanitizer
{
    private static readonly ConcurrentLfu<(string, char, bool?), string> Cache = new(1 << 16);

    public static string Sanitize(string symbol, char escapedChar = 'X', bool? includeHash = null)
    {
        var cacheKey = (symbol, escapedChar, includeHash);
        if (Cache.TryGet(cacheKey, out var escaped) && escaped is not null)
        {
            return escaped;
        }

        var sb = new StringBuilder(symbol.Length);
        var span = (ReadOnlySpan<char>)symbol;
        span = span.Trim();
        var altered = false;
        foreach (var c in span)
        {
            switch (c)
            {
                case (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_' or '-':
                    sb.Append(c);
                    break;
                default:
                    sb.Append(escapedChar);
                    altered = true;
                    break;
            }
        }

        if (includeHash.GetValueOrDefault(altered))
        {
            sb.Append(CultureInfo.InvariantCulture, $"{PairHasher.Hash(symbol):x8}");
        }

        var res = sb.ToString();
        Cache.AddOrUpdate(cacheKey, res);
        return res;
    }
}