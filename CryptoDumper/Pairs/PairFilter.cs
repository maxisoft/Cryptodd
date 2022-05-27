using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Maxisoft.Utils.Collections.Dictionaries;
using Maxisoft.Utils.Collections.Dictionaries.Specialized;
using Maxisoft.Utils.Collections.LinkedLists;
using Maxisoft.Utils.Empties;

namespace CryptoDumper.Pairs;

public readonly struct PairFilterEntry
{
    public readonly string Text;
    public readonly Regex? Regex;

    internal PairFilterEntry(string text, Regex? regex = null)
    {
        Text = text;
        Regex = regex;
    }

    public bool IsRegex => Regex is { };

    public bool Match(string s)
    {
        if (Regex is { })
        {
            return Regex!.IsMatch(s);
        }

        return Text == s;
    }
}

public interface IPairFilter
{
    bool Match(string input);
}

public class PairFilter : IPairFilter
{
    public static readonly Regex DetectRegex = new(@"^[a-zA-Z][\w:/-]+$",
        RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private ConcurrentDictionary<string, EmptyStruct> _pairsSet = new(StringComparer.InvariantCultureIgnoreCase);
    private readonly ConcurrentDictionary<string, EmptyStruct> _noMatchPairsSet = new(StringComparer.InvariantCultureIgnoreCase);

    private LinkedListAsIList<PairFilterEntry> _entries = new();
    private static readonly char[] Separator = { '\r', '\n', ';' };
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();


    public void AddAll(string input, bool allowRegex = true)
    {
        var es = new EmptyStruct();
        foreach (var s in input.Split(Separator,
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (s.StartsWith('#') || s.StartsWith("//", StringComparison.InvariantCulture)) continue;
            if (_pairsSet.TryAdd(s, es) && allowRegex && !DetectRegex.IsMatch(s))
            {
                lock (_entries)
                {
                    _entries.AddLast(new PairFilterEntry(s, BuildRegex(s)));
                }
            }
        }
        _noMatchPairsSet.Clear();
    }

    private static Regex BuildRegex(string input)
    {
        if (RegexCache.TryGetValue(input, out var regex))
        {
            return regex;
        }

        regex = new Regex(input,
            RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.CultureInvariant |
            RegexOptions.IgnoreCase);
        return RegexCache.TryAdd(input, regex) ? regex : RegexCache[input];
    }

    internal void CopyFrom(PairFilter other)
    {
        if (ReferenceEquals(this, other)) return;
        // it's a shallow copy so a AddAll() call populate both this and other
        _pairsSet = other._pairsSet;
        _entries = other._entries;
        _noMatchPairsSet.Clear();
    }

    public bool Match(string input)
    {
        if (_pairsSet.ContainsKey(input)) return true;
        if (_noMatchPairsSet.ContainsKey(input)) return false;
        var node = _entries.First;
        if (node is {Next: null, Value.Text: ".*" }) return true;
        while (node is {})
        {
            if (node.Value.Match(input))
            {
                _pairsSet.TryAdd(input, default); // cache the result to avoid using regex again
                return true;
            }

            node = node.Next;
        }

        if (!_entries.Any()) // empty filter mean match any input string
        {
            return true;
        }
        
        _noMatchPairsSet.TryAdd(input, default);
        return false;
    }
}