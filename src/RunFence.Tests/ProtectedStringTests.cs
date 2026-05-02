using System.Runtime.InteropServices;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class ProtectedStringTests
{
    private static void AssertContent(ProtectedString ps, string expected)
    {
        Assert.Equal(expected.Length, ps.Length);
        var ptr = ps.AllocUnicode();
        try
        {
            Assert.Equal(expected, Marshal.PtrToStringUni(ptr));
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }

    [Fact]
    public void DefaultConstructor_CreatesEmptyString()
    {
        using var ps = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);

        Assert.Equal(0, ps.Length);
        Assert.False(ps.IsReadOnly);
    }

    [Fact]
    public void Constructor_FromSpan_StoresCorrectContent()
    {
        using var ps = new ProtectedString("Hello".AsSpan(), protect: false);

        Assert.Equal(5, ps.Length);
        Assert.False(ps.IsReadOnly);
        AssertContent(ps, "Hello");
    }

    [Fact]
    public void FromChars_Array_ProducesReadOnlyInstance()
    {
        using var ps = ProtectedString.FromChars("test".ToCharArray());

        Assert.Equal(4, ps.Length);
        Assert.True(ps.IsReadOnly);
    }

    [Fact]
    public void FromChars_Span_ProducesReadOnlyInstance()
    {
        using var ps = ProtectedString.FromChars("test".AsSpan());

        Assert.Equal(4, ps.Length);
        Assert.True(ps.IsReadOnly);
    }

    [Fact]
    public void AppendChar_IncreasesLength()
    {
        using var ps = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);

        ps.AppendChar('A');
        ps.AppendChar('B');
        ps.AppendChar('C');

        AssertContent(ps, "ABC");
    }

    [Theory]
    [InlineData("BC", 0, 'A', "ABC")]
    [InlineData("AC", 1, 'B', "ABC")]
    [InlineData("AB", 2, 'C', "ABC")]
    public void InsertAt_InsertsCharAtPosition(string initial, int index, char ch, string expected)
    {
        using var ps = new ProtectedString(initial.AsSpan(), protect: false);

        ps.InsertAt(index, ch);

        AssertContent(ps, expected);
    }

    [Theory]
    [InlineData("ABC", 0, "BC")]
    [InlineData("ABC", 1, "AC")]
    [InlineData("ABC", 2, "AB")]
    public void RemoveAt_RemovesCharAtPosition(string initial, int index, string expected)
    {
        using var ps = new ProtectedString(initial.AsSpan(), protect: false);

        ps.RemoveAt(index);

        AssertContent(ps, expected);
    }

    [Theory]
    [InlineData("AXC", 1, 'B', "ABC")]
    [InlineData("XBC", 0, 'A', "ABC")]
    [InlineData("ABX", 2, 'C', "ABC")]
    public void SetAt_ReplacesCharAtPosition(string initial, int index, char ch, string expected)
    {
        using var ps = new ProtectedString(initial.AsSpan(), protect: false);

        ps.SetAt(index, ch);

        AssertContent(ps, expected);
    }

    [Fact]
    public void Clear_ResetsToEmpty()
    {
        using var ps = new ProtectedString("Hello".AsSpan(), protect: false);

        ps.Clear();

        Assert.Equal(0, ps.Length);
    }

    [Fact]
    public void Copy_CreatesDeepCopy()
    {
        using var original = new ProtectedString("Hello".AsSpan(), protect: false);
        original.MakeReadOnly();

        using var copy = original.Copy();

        // Copy is mutable even if source is read-only
        Assert.False(copy.IsReadOnly);
        Assert.Equal(5, copy.Length);

        // Modifying copy doesn't affect original
        copy.AppendChar('!');
        Assert.Equal(5, original.Length);
        AssertContent(copy, "Hello!");
    }

    [Fact]
    public void MakeReadOnly_BlocksMutations()
    {
        using var ps = new ProtectedString("Test".AsSpan(), protect: false);
        ps.MakeReadOnly();

        Assert.True(ps.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => ps.AppendChar('X'));
        Assert.Throws<InvalidOperationException>(() => ps.InsertAt(0, 'X'));
        Assert.Throws<InvalidOperationException>(() => ps.RemoveAt(0));
        Assert.Throws<InvalidOperationException>(() => ps.SetAt(0, 'X'));
        Assert.Throws<InvalidOperationException>(() => ps.Clear());
    }

    [Fact]
    public void Dispose_PreventsFurtherOperations()
    {
        var ps = new ProtectedString("Test".AsSpan(), protect: false);
        ps.Dispose();

        Assert.Throws<ObjectDisposedException>(() => ps.Length);
        Assert.Throws<ObjectDisposedException>(() => ps.AppendChar('X'));
        Assert.Throws<ObjectDisposedException>(() => ps.AllocUnicode());
        Assert.Throws<ObjectDisposedException>(() => ps.ToBSTR());
        Assert.Throws<ObjectDisposedException>(() => ps.Copy());
        Assert.Throws<ObjectDisposedException>(() => ps.MakeReadOnly());
        Assert.Throws<ObjectDisposedException>(() => ps.IsReadOnly);
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        var ps = new ProtectedString("Test".AsSpan(), protect: false);
        ps.Dispose();
        ps.Dispose(); // Should not throw
    }

    [Fact]
    public void CapacityGrowth_AcrossBlockBoundary()
    {
        using var ps = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);

        // Append 9 chars = 18 bytes → exceeds initial 16-byte capacity
        for (int i = 0; i < 9; i++)
            ps.AppendChar((char)('A' + i));

        AssertContent(ps, "ABCDEFGHI");
    }

    [Fact]
    public void AllocUnicode_EmptyString_ReturnsNullTerminated()
    {
        using var ps = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);

        var ptr = ps.AllocUnicode();
        try
        {
            Assert.Equal("", Marshal.PtrToStringUni(ptr));
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }

    [Fact]
    public void AllocUnicode_ReturnsCorrectString()
    {
        using var ps = new ProtectedString("P@ss\u00e9!".AsSpan(), protect: false);

        var ptr = ps.AllocUnicode();
        try
        {
            Assert.Equal("P@ss\u00e9!", Marshal.PtrToStringUni(ptr));
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }

    [Fact]
    public void ToBSTR_ReturnsValidBSTR()
    {
        using var ps = new ProtectedString("Hello".AsSpan(), protect: false);

        var bstr = ps.ToBSTR();
        try
        {
            Assert.Equal("Hello", Marshal.PtrToStringBSTR(bstr));
            // Verify BSTR length prefix (4 bytes before pointer, contains byte length)
            int byteLen = Marshal.ReadInt32(bstr, -4);
            Assert.Equal(10, byteLen); // 5 chars * 2 bytes
        }
        finally
        {
            Marshal.ZeroFreeBSTR(bstr);
        }
    }

    [Fact]
    public void ToBSTR_EmptyString_ReturnsValidBSTR()
    {
        using var ps = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);

        var bstr = ps.ToBSTR();
        try
        {
            Assert.Equal("", Marshal.PtrToStringBSTR(bstr));
            int byteLen = Marshal.ReadInt32(bstr, -4);
            Assert.Equal(0, byteLen);
        }
        finally
        {
            Marshal.ZeroFreeBSTR(bstr);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void InsertAt_InvalidIndex_Throws(int index)
    {
        using var ps = new ProtectedString("Test".AsSpan(), protect: false);
        Assert.Throws<ArgumentOutOfRangeException>(() => ps.InsertAt(index, 'X'));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void RemoveAt_InvalidIndex_Throws(int index)
    {
        using var ps = new ProtectedString("Test".AsSpan(), protect: false);
        Assert.Throws<ArgumentOutOfRangeException>(() => ps.RemoveAt(index));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void SetAt_InvalidIndex_Throws(int index)
    {
        using var ps = new ProtectedString("Test".AsSpan(), protect: false);
        Assert.Throws<ArgumentOutOfRangeException>(() => ps.SetAt(index, 'X'));
    }

    // ── ContentEqual ─────────────────────────────────────────────────────────

    [Fact]
    public void ContentEqual_BothNull_ReturnsTrue()
    {
        Assert.True(ProtectedString.ContentEqual(null, null));
    }

    [Fact]
    public void ContentEqual_OneNull_ReturnsFalse()
    {
        using var ps = new ProtectedString("abc".AsSpan(), protect: false);
        Assert.False(ProtectedString.ContentEqual(ps, null));
        Assert.False(ProtectedString.ContentEqual(null, ps));
    }

    [Fact]
    public void ContentEqual_DifferentLengths_ReturnsFalse()
    {
        using var a = new ProtectedString("abc".AsSpan(), protect: false);
        using var b = new ProtectedString("abcd".AsSpan(), protect: false);
        Assert.False(ProtectedString.ContentEqual(a, b));
    }

    [Fact]
    public void ContentEqual_SameContent_ReturnsTrue()
    {
        using var a = new ProtectedString("P@ssw0rd".AsSpan(), protect: false);
        using var b = new ProtectedString("P@ssw0rd".AsSpan(), protect: false);
        Assert.True(ProtectedString.ContentEqual(a, b));
    }

    [Fact]
    public void ContentEqual_DifferentContentSameLength_ReturnsFalse()
    {
        using var a = new ProtectedString("Pass1234".AsSpan(), protect: false);
        using var b = new ProtectedString("Pass1235".AsSpan(), protect: false);
        Assert.False(ProtectedString.ContentEqual(a, b));
    }

    [Fact]
    public void ContentEqual_BothEmpty_ReturnsTrue()
    {
        using var a = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);
        using var b = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);
        Assert.True(ProtectedString.ContentEqual(a, b));
    }

    // ── CreateEmpty ───────────────────────────────────────────────────────────

    [Fact]
    public void CreateEmpty_ReturnsNonNullReadOnlyEmptyInstance()
    {
        using var ps = ProtectedString.CreateEmpty();

        Assert.NotNull(ps);
        Assert.Equal(0, ps.Length);
        Assert.True(ps.IsReadOnly);
    }

    // ── CharAt ────────────────────────────────────────────────────────────────

    [Fact]
    public void CharAt_ReturnsCorrectChar()
    {
        using var ps = new ProtectedString("Hello".AsSpan(), protect: false);

        Assert.Equal('H', ps.CharAt(0));
        Assert.Equal('e', ps.CharAt(1));
        Assert.Equal('l', ps.CharAt(2));
        Assert.Equal('l', ps.CharAt(3));
        Assert.Equal('o', ps.CharAt(4));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void CharAt_OutOfRange_Throws(int index)
    {
        using var ps = new ProtectedString("Hello".AsSpan(), protect: false);
        Assert.Throws<ArgumentOutOfRangeException>(() => ps.CharAt(index));
    }

    [Fact]
    public void CharAt_SurrogatePair_ReturnsBothHalves()
    {
        // U+1F511 (🔑) encoded as two UTF-16 code units: high surrogate \uD83D, low surrogate \uDD11
        const char high = '\uD83D';
        const char low = '\uDD11';
        using var ps = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);
        ps.InsertAt(0, high);
        ps.InsertAt(1, low);

        Assert.Equal(2, ps.Length);
        Assert.Equal(high, ps.CharAt(0));
        Assert.Equal(low, ps.CharAt(1));
    }

    [Fact]
    public void InsertAt_SurrogatePairAtomically_TwoCodeUnits()
    {
        // Insert both halves of a surrogate pair; both must be retrievable independently
        const char high = '\uD83D';
        const char low = '\uDD11';
        using var ps = new ProtectedString("AB".AsSpan(), protect: false);
        ps.InsertAt(1, high);
        ps.InsertAt(2, low);

        Assert.Equal(4, ps.Length);
        Assert.Equal('A', ps.CharAt(0));
        Assert.Equal(high, ps.CharAt(1));
        Assert.Equal(low, ps.CharAt(2));
        Assert.Equal('B', ps.CharAt(3));
    }

    [Fact]
    public void RemoveAt_SurrogatePairTwice_RemovesBothCodeUnits()
    {
        const char high = '\uD83D';
        const char low = '\uDD11';
        using var ps = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);
        ps.InsertAt(0, high);
        ps.InsertAt(1, low);

        // Remove both halves: first RemoveAt(0) removes high (low shifts to 0),
        // second RemoveAt(0) removes low.
        ps.RemoveAt(0);
        ps.RemoveAt(0);

        Assert.Equal(0, ps.Length);
    }
}
