using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Cryptodd.Databases.Tables;

public static class TableNameStandardize
{
    private static readonly Lazy<Regex> _valid = new(() =>
        new Regex(@"^[A-Za-z][A-Za-z0-9_]{0,127}$", RegexOptions.Compiled | RegexOptions.CultureInvariant));

    public static char[] EscapeChars = CreateEscapeChars();

    private static char[] CreateEscapeChars()
    {
        var res = new[] { '.', '/', ' ', ':', '@', '$', '\\', '"' };
        Array.Sort(res);
        return res;
    }

    public static bool IsValid(string table) => _valid.Value.Match(table).Success;

    [MethodImpl(MethodImplOptions.AggressiveOptimization)]
    public static string Standardize(string table)
    {
        table = table.Normalize(NormalizationForm.FormKD);
        var stringBuilder = new StringBuilder(table.Length);

        foreach (var c in from c in table
                 let unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c)
                 where unicodeCategory != UnicodeCategory.NonSpacingMark
                 select c)
        {
            stringBuilder.Append(Array.BinarySearch(EscapeChars, c) >= 0 ? '_' : c);
        }

        table = stringBuilder
            .ToString()
            .Normalize(NormalizationForm.FormKC);

        Debug.Assert(IsValid(table));

        return table;
    }
}