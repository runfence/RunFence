using System.Runtime.InteropServices;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class SecurePasswordBoxTests
{
    private const int WM_CHAR = 0x0102;
    private const int WM_PASTE = 0x0302;
    private const int WM_COPY = 0x0301;
    private const int WM_CUT = 0x0300;
    private const int WM_CLEAR = 0x0303;
    private const int WM_UNDO = 0x0304;
    private const char BulletChar = '\u25CF';

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [Fact]
    public void InitialState_IsEmpty_True_Length_Zero()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tb = new TextBox();
            using var spb = new SecurePasswordBox(tb);

            Assert.True(spb.IsEmpty);
            Assert.Equal(0, spb.GetPasswordLength());
        });
    }

    [Fact]
    public void SetFromProtectedString_GetPassword_Roundtrip()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tb = new TextBox();
            using var spb = new SecurePasswordBox(tb);
            using var value = ProtectedString.FromChars("S3cur3P@ss".AsSpan());

            spb.SetFromProtectedString(value);

            using var result = spb.GetPassword();
            Assert.True(ProtectedString.ContentEqual(value, result));
        });
    }

    [Fact]
    public void SetFromProtectedString_GetPasswordLength_ReturnsCorrectLength()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tb = new TextBox();
            using var spb = new SecurePasswordBox(tb);
            using var value = ProtectedString.FromChars("Hello123".AsSpan());

            spb.SetFromProtectedString(value);

            Assert.Equal(8, spb.GetPasswordLength());
        });
    }

    [Fact]
    public void Clear_AfterSetFromProtectedString_IsEmpty_True()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tb = new TextBox();
            using var spb = new SecurePasswordBox(tb);
            using var value = ProtectedString.FromChars("Pass".AsSpan());
            spb.SetFromProtectedString(value);

            spb.Clear();

            Assert.True(spb.IsEmpty);
        });
    }

    [Fact]
    public void PasswordsMatch_EqualContent_ReturnsTrue()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tb1 = new TextBox();
            using var tb2 = new TextBox();
            using var spb1 = new SecurePasswordBox(tb1);
            using var spb2 = new SecurePasswordBox(tb2);
            using var value = ProtectedString.FromChars("SameP@ss".AsSpan());

            spb1.SetFromProtectedString(value);
            spb2.SetFromProtectedString(value);

            Assert.True(spb1.PasswordsMatch(spb2));
        });
    }

    [Fact]
    public void PasswordsMatch_DifferentContent_ReturnsFalse()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tb1 = new TextBox();
            using var tb2 = new TextBox();
            using var spb1 = new SecurePasswordBox(tb1);
            using var spb2 = new SecurePasswordBox(tb2);
            using var val1 = ProtectedString.FromChars("Pass1".AsSpan());
            using var val2 = ProtectedString.FromChars("Pass2".AsSpan());

            spb1.SetFromProtectedString(val1);
            spb2.SetFromProtectedString(val2);

            Assert.False(spb1.PasswordsMatch(spb2));
        });
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tb = new TextBox();
            var spb = new SecurePasswordBox(tb);

            spb.Dispose();
            spb.Dispose();
        });
    }

    // ── Surrogate pair handling ───────────────────────────────────────────────

    [Fact]
    public void SetFromProtectedString_SurrogatePair_RoundtripsCorrectly()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tb = new TextBox();
            using var spb = new SecurePasswordBox(tb);

            // Build a ProtectedString containing a surrogate pair
            const char high = '\uD83D';
            const char low = '\uDD11';
            using var value = new ProtectedString(ReadOnlySpan<char>.Empty, protect: false);
            value.InsertAt(0, high);
            value.InsertAt(1, low);
            value.MakeReadOnly();

            spb.SetFromProtectedString(value);

            Assert.Equal(2, spb.GetPasswordLength());

            using var result = spb.GetPassword();
            Assert.True(ProtectedString.ContentEqual(value, result));
        });
    }

    [Fact]
    public void WmChar_TypingUpdatesPasswordAndDisplaysOnlyBullets()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tb = new TextBox();
            using var spb = new SecurePasswordBox(tb);
            _ = tb.Handle;

            SendMessage(tb.Handle, WM_CHAR, (IntPtr)'a', IntPtr.Zero);
            SendMessage(tb.Handle, WM_CHAR, (IntPtr)'b', IntPtr.Zero);

            Assert.Equal(new string(BulletChar, 2), tb.Text);
            Assert.Equal("ab", ProtectedStringToString(spb.GetPassword()));
        });
    }

    [Fact]
    public void WmPaste_PastesIntoBufferWithoutPlainTextDisplay()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tb = new TextBox();
            using var spb = new SecurePasswordBox(tb, FakeSecurePasswordClipboardService.WithText("xy"));
            _ = tb.Handle;

            SendMessage(tb.Handle, WM_PASTE, IntPtr.Zero, IntPtr.Zero);

            Assert.Equal(new string(BulletChar, 2), tb.Text);
            Assert.Equal("xy", ProtectedStringToString(spb.GetPassword()));
        });
    }

    [Fact]
    public void WmPaste_EmptyUnicodeText_ReplacesSelection()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tb = new TextBox();
            using var spb = new SecurePasswordBox(tb, FakeSecurePasswordClipboardService.WithText(""));
            _ = tb.Handle;

            SendMessage(tb.Handle, WM_CHAR, (IntPtr)'a', IntPtr.Zero);
            SendMessage(tb.Handle, WM_CHAR, (IntPtr)'b', IntPtr.Zero);
            tb.SelectionStart = 0;
            tb.SelectionLength = 2;

            SendMessage(tb.Handle, WM_PASTE, IntPtr.Zero, IntPtr.Zero);

            Assert.True(spb.IsEmpty);
            Assert.Equal("", tb.Text);
        });
    }

    [Fact]
    public void WmCopyCutClearUndo_AreRoutedWithoutExposingHiddenPassword()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tb = new TextBox();
            var clipboard = FakeSecurePasswordClipboardService.WithText(null);
            using var spb = new SecurePasswordBox(tb, clipboard);
            _ = tb.Handle;

            SendMessage(tb.Handle, WM_CHAR, (IntPtr)'a', IntPtr.Zero);
            SendMessage(tb.Handle, WM_CHAR, (IntPtr)'b', IntPtr.Zero);

            tb.SelectionStart = 0;
            tb.SelectionLength = 2;
            SendMessage(tb.Handle, WM_COPY, IntPtr.Zero, IntPtr.Zero);
            Assert.Equal("ab", ProtectedStringToString(spb.GetPassword()));
            Assert.Equal(1, clipboard.SuppressPasswordClipboardWriteChecks);

            SendMessage(tb.Handle, WM_CUT, IntPtr.Zero, IntPtr.Zero);
            Assert.Equal("ab", ProtectedStringToString(spb.GetPassword()));
            Assert.Equal(2, clipboard.SuppressPasswordClipboardWriteChecks);

            SendMessage(tb.Handle, WM_CLEAR, IntPtr.Zero, IntPtr.Zero);
            Assert.True(spb.IsEmpty);

            SendMessage(tb.Handle, WM_CHAR, (IntPtr)'z', IntPtr.Zero);
            tb.SelectionStart = 0;
            tb.SelectionLength = 1;
            SendMessage(tb.Handle, WM_CLEAR, IntPtr.Zero, IntPtr.Zero);
            SendMessage(tb.Handle, WM_UNDO, IntPtr.Zero, IntPtr.Zero);

            Assert.True(spb.IsEmpty);
            Assert.Equal("", tb.Text);
        });
    }

    [Fact]
    public void WmCopyCut_AreSuppressedEvenWhenRevealed()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tb = new TextBox { Width = 100, Height = 24 };
            var clipboard = FakeSecurePasswordClipboardService.WithText(null);
            using var spb = new SecurePasswordBox(tb, clipboard);
            spb.AddEyeToggle();
            _ = tb.Handle;

            SendMessage(tb.Handle, WM_CHAR, (IntPtr)'a', IntPtr.Zero);
            SendMessage(tb.Handle, WM_CHAR, (IntPtr)'b', IntPtr.Zero);
            ((Button)tb.Controls[0]).PerformClick();
            tb.SelectionStart = 0;
            tb.SelectionLength = 2;

            SendMessage(tb.Handle, WM_COPY, IntPtr.Zero, IntPtr.Zero);
            SendMessage(tb.Handle, WM_CUT, IntPtr.Zero, IntPtr.Zero);

            Assert.Equal(2, clipboard.SuppressPasswordClipboardWriteChecks);
            Assert.Equal("ab", ProtectedStringToString(spb.GetPassword()));
        });
    }

    private static string ProtectedStringToString(ProtectedString value)
    {
        using (value)
        {
            var ptr = value.AllocUnicode();
            try
            {
                return Marshal.PtrToStringUni(ptr) ?? "";
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(ptr);
            }
        }
    }

    private sealed class FakeSecurePasswordClipboardService : ISecurePasswordClipboardService
    {
        private readonly string? _text;
        private readonly bool _unicodeTextAvailable;

        private FakeSecurePasswordClipboardService(string? text, bool unicodeTextAvailable)
        {
            _text = text;
            _unicodeTextAvailable = unicodeTextAvailable;
        }

        public static FakeSecurePasswordClipboardService WithText(string? text) =>
            new(text, text is not null);

        public int SuppressPasswordClipboardWriteChecks { get; private set; }

        public ISecurePasswordPasteSession? OpenUnicodeText(out bool unicodeTextAvailable)
        {
            unicodeTextAvailable = _unicodeTextAvailable;
            return _text is null ? null : new FakeSecurePasswordPasteSession(_text);
        }

        public bool ShouldSuppressPasswordClipboardWrite()
        {
            SuppressPasswordClipboardWriteChecks++;
            return true;
        }
    }

    private sealed class FakeSecurePasswordPasteSession : ISecurePasswordPasteSession
    {
        private readonly string _text;

        public FakeSecurePasswordPasteSession(string text)
        {
            _text = text;
        }

        public char ReadChar(int charIndex) =>
            charIndex < _text.Length ? _text[charIndex] : '\0';

        public void Dispose()
        {
        }
    }
}
