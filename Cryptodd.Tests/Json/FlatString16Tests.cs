using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Cryptodd.Json;
using Xunit;

namespace Cryptodd.Tests.Json;

public class FlatString16Tests
{
    [Fact]
    public void TestEqual()
    {
        Assert.True(((FlatString16) "") == ((FlatString16) string.Empty));
        // ReSharper disable once EqualExpressionComparison
        Assert.True(((FlatString16) "test") == ((FlatString16) "test"));
        Assert.True(((FlatString16) "test") != ((FlatString16) "testb"));
        Assert.True(((FlatString16) "test") != ((FlatString16) "testc"));
        Assert.True(((FlatString16) "abc") != ((FlatString16) "cde"));
        Assert.True(((FlatString16) nameof(FlatString16Tests)[..FlatString16.MaxLength]) != ((FlatString16) "cde"));
        Assert.True((((FlatString16) "cde") != (FlatString16) nameof(FlatString16Tests)[..FlatString16.MaxLength]));
        Assert.True((((FlatString16) nameof(FlatString16Tests)[..FlatString16.MaxLength].ToUpperInvariant()) != (FlatString16) nameof(FlatString16Tests)[..FlatString16.MaxLength]));
        Assert.True((((FlatString16) nameof(FlatString16Tests)[..FlatString16.MaxLength].ToUpperInvariant()) == (FlatString16) nameof(FlatString16Tests)[..FlatString16.MaxLength].ToUpperInvariant()));
    }

    [Fact]
    public void TestStatic()
    {
        Assert.False(RuntimeHelpers.IsReferenceOrContainsReferences<FlatString16>());
        Assert.Equal(sizeof(byte) + 16 * sizeof(byte), Marshal.SizeOf<FlatString16>());
    }
}