using BitFaster.Caching.Lfu;
using Maxisoft.Utils.Collections.Lists;

namespace Cryptodd.Json;

public class StringPool
{
    private struct ValueStruct
    {
        internal int HashCode { get; set; }
        internal FlatString16 flatString { get; set; }
        internal string? String { get; set; }
    }

    private readonly ConcurrentLfu<int, ArrayList<ValueStruct>> _lfu;

    public StringPool(int lfuSize)
    {
        _lfu = new ConcurrentLfu<int, ArrayList<ValueStruct>>(lfuSize);
    }

    public bool TryGetString(ref FlatString16 s16, out string res)
    {
        var h = s16.GetHashCode();
        ArrayList<ValueStruct>? arr;
        if (!_lfu.TryGet(h, out arr))
        {
            arr ??= new ArrayList<ValueStruct>();
            _lfu.TryUpdate(h, arr);
        }

        foreach (ref var valueStruct in arr)
        {
            if (valueStruct.HashCode != h || !valueStruct.flatString.Equals(ref s16))
            {
                continue;
            }

            valueStruct.String ??= s16.ToString();
            res = valueStruct.String;
            return true;
        }

        res = s16.ToString();
        lock (arr)
        {
            arr.Add(new ValueStruct() { flatString = s16, HashCode = h, String = res });
        }
        return true;
    }

    public bool TryGetString(ReadOnlySpan<byte> span, out string res)
    {
        if (span.Length > FlatString16.MaxLength)
        {
            res = "";
            return false;
        }

        FlatString16 s16 = span;
        return TryGetString(ref s16, out res);
    }

    public bool TryGetString(ReadOnlySpan<char> span, out string res)
    {
        if (span.Length > FlatString16.MaxLength)
        {
            res = "";
            return false;
        }

        FlatString16 s16 = span;
        return TryGetString(ref s16, out res);
    }
}