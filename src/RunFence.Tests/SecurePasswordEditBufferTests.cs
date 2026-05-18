using RunFence.Account.UI;
using RunFence.Core;
using System.Runtime.InteropServices;
using Xunit;

namespace RunFence.Tests;

public class SecurePasswordEditBufferTests
{
    [Fact]
    public void ApplyChar_InsertsAtCursor()
    {
        using var buffer = new SecurePasswordEditBuffer();

        var first = buffer.ApplyChar('a', 0, 0, 0);
        var second = buffer.ApplyChar('c', first.SelectionStart, 0, 0);
        buffer.ApplyChar('b', 1, 0, 0);

        Assert.Equal(2, second.SelectionStart);
        Assert.Equal("abc", ToString(buffer));
    }

    [Fact]
    public void ApplyChar_ReplacesSelection()
    {
        using var buffer = FromText("abc");

        var result = buffer.ApplyChar('X', 1, 1, 0);

        Assert.Equal(2, result.SelectionStart);
        Assert.Equal("aXc", ToString(buffer));
    }

    [Fact]
    public void ApplyBackspace_RemovesSelectionOrPreviousChar()
    {
        using var buffer = FromText("abcd");

        var selection = buffer.ApplyBackspace(1, 2, 0);
        var previous = buffer.ApplyBackspace(selection.SelectionStart, 0, 0);

        Assert.Equal(0, previous.SelectionStart);
        Assert.Equal("d", ToString(buffer));
    }

    [Fact]
    public void ApplyDelete_RemovesSelectionOrNextChar()
    {
        using var buffer = FromText("abcd");

        buffer.ApplyDelete(1, 2, 0);
        buffer.ApplyDelete(0, 0, 0);

        Assert.Equal("d", ToString(buffer));
    }

    [Fact]
    public void ApplyChar_RespectsMaxLength()
    {
        using var buffer = FromText("ab");

        var result = buffer.ApplyChar('c', 2, 0, 2);

        Assert.False(result.Changed);
        Assert.Equal("ab", ToString(buffer));
    }

    [Fact]
    public void ApplyChar_InsertsSurrogatePairAtomically()
    {
        using var buffer = new SecurePasswordEditBuffer();

        var high = buffer.ApplyChar('\uD83D', 0, 0, 0);
        var low = buffer.ApplyChar('\uDD11', high.SelectionStart, 0, 0);

        Assert.True(low.Changed);
        Assert.Equal(2, low.SelectionStart);
        Assert.Equal("\uD83D\uDD11", ToString(buffer));
    }

    [Fact]
    public void ApplyBackspace_RemovesSurrogatePairTogether()
    {
        using var buffer = FromText("\uD83D\uDD11x");

        buffer.ApplyBackspace(2, 0, 0);

        Assert.Equal("x", ToString(buffer));
    }

    [Fact]
    public void ApplyPaste_TruncatesAtMaxLengthAndPreservesPairs()
    {
        using var buffer = FromText("a");

        var result = buffer.ApplyPaste(new TestPasteSource("\uD83D\uDD11bc"), 1, 0, 3);

        Assert.Equal(3, result.SelectionStart);
        Assert.Equal("a\uD83D\uDD11", ToString(buffer));
    }

    [Fact]
    public void ApplyClear_RemovesOnlySelection()
    {
        using var buffer = FromText("abcd");

        buffer.ApplyClear(1, 2);

        Assert.Equal("ad", ToString(buffer));
    }

    [Fact]
    public void ApplyCopyCutUndo_DoNotChangeBuffer()
    {
        using var buffer = FromText("abcd");

        var copy = buffer.ApplyCopy(1, 2);
        var cut = buffer.ApplyCut(1, 2);
        var undo = buffer.ApplyUndo(1, 2);

        Assert.False(copy.Changed);
        Assert.False(cut.Changed);
        Assert.False(undo.Changed);
        Assert.Equal("abcd", ToString(buffer));
    }

    private static SecurePasswordEditBuffer FromText(string text)
    {
        var buffer = new SecurePasswordEditBuffer();
        using var password = new ProtectedString(text.AsSpan(), protect: false);
        buffer.SetFromProtectedString(password);
        return buffer;
    }

    private static string ToString(SecurePasswordEditBuffer buffer)
    {
        using var password = buffer.GetPassword();
        return password.UseUnicodeSnapshot(snapshot =>
            Marshal.PtrToStringUni(snapshot.DangerousGetIntPtr(), snapshot.CharCount) ?? string.Empty);
    }

    private sealed class TestPasteSource(string text) : ISecurePasswordPasteSource
    {
        public char ReadChar(int charIndex) => charIndex < text.Length ? text[charIndex] : '\0';
    }
}
