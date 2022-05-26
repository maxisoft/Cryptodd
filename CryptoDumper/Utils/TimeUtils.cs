using System.Runtime.CompilerServices;

namespace CryptoDumper.Utils;

public static class TimeUtils
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long ToUnixTimeMilliseconds(this DateTime dateTime)
    {
        return ((DateTimeOffset)dateTime).ToUnixTimeMilliseconds();
    }
}