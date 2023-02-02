using System.Globalization;
using System.Text.RegularExpressions;

namespace Cryptodd.Okx.Options;

internal static class OkxOptionInstrumentIdParser
{
    private static readonly Regex ParseRegex = new(
        @"^(?<uly>[\w-]+?)-(?<date>[0-9]{6}?)-(?<price>(?:[0-9]+\.)?[0-9]+?)-(?<side>P|C)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant |
        RegexOptions.NonBacktracking | RegexOptions.IgnoreCase);

    public static bool TryParse(string value, out OkxOptionInstrumentId instrumentId)
    {
        var match = ParseRegex.Match(value);
        if (!match.Success)
        {
            instrumentId = default!;
            return false;
        }

        var uly = match.Groups["uly"].Value;
        var date = match.Groups["date"].ValueSpan;
        if (!double.TryParse(match.Groups["price"].ValueSpan, NumberFormatInfo.InvariantInfo, out var price))
        {
            instrumentId = default!;
            return false;
        }

        if (!int.TryParse(date[..2], NumberFormatInfo.InvariantInfo, out var year))
        {
            instrumentId = default!;
            return false;
        }

        if (!sbyte.TryParse(date[2..4], NumberFormatInfo.InvariantInfo, out var month))
        {
            instrumentId = default!;
            return false;
        }

        if (!sbyte.TryParse(date[4..], NumberFormatInfo.InvariantInfo, out var day))
        {
            instrumentId = default!;
            return false;
        }

        OkxOptionSide side;
        switch (match.Groups["side"].ValueSpan[0])
        {
            case 'P':
            case 'p':
                side = OkxOptionSide.Put;
                break;
            case 'C':
            case 'c':
                side = OkxOptionSide.Call;
                break;
            default:
                instrumentId = default!;
                return false;
        }

        instrumentId = new OkxOptionInstrumentId(uly, year, month, day, price, side);
        return true;
    }
}