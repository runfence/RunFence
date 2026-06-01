using System.Runtime.InteropServices;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Tests.Helpers;
using RunFence.UI.Forms;
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
    public void HostedByContextHelpForm_RegistersAndUnregistersSnapshotParticipant()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var host = new ContextHelpForm();
            using var tb = new TextBox();
            host.Controls.Add(tb);
            using var spb = new SecurePasswordBox(tb);
            StaTestHelper.CreateControlTree(host);

            Assert.Contains(host.GetContextHelpSnapshotParticipants(), participant => ReferenceEquals(participant, spb));

            spb.Dispose();

            Assert.DoesNotContain(host.GetContextHelpSnapshotParticipants(), participant => ReferenceEquals(participant, spb));
        });
    }

    [Fact]
    public void MovingBetweenContextHelpForms_UpdatesSnapshotParticipantRegistration()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var firstHost = new ContextHelpForm();
            using var secondHost = new ContextHelpForm();
            using var tb = new TextBox();
            firstHost.Controls.Add(tb);
            using var spb = new SecurePasswordBox(tb);
            StaTestHelper.CreateControlTree(firstHost);
            StaTestHelper.CreateControlTree(secondHost);

            Assert.Contains(firstHost.GetContextHelpSnapshotParticipants(), participant => ReferenceEquals(participant, spb));

            firstHost.Controls.Remove(tb);

            Assert.DoesNotContain(firstHost.GetContextHelpSnapshotParticipants(), participant => ReferenceEquals(participant, spb));

            secondHost.Controls.Add(tb);

            Assert.Contains(secondHost.GetContextHelpSnapshotParticipants(), participant => ReferenceEquals(participant, spb));
        });
    }

    [Fact]
    public void MovingAncestorContainerBetweenContextHelpForms_UpdatesSnapshotParticipantRegistration()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var firstHost = new ContextHelpForm();
            using var secondHost = new ContextHelpForm();
            using var panel = new Panel();
            using var tb = new TextBox();
            panel.Controls.Add(tb);
            firstHost.Controls.Add(panel);
            using var spb = new SecurePasswordBox(tb);
            StaTestHelper.CreateControlTree(firstHost);
            StaTestHelper.CreateControlTree(secondHost);

            Assert.Contains(firstHost.GetContextHelpSnapshotParticipants(), participant => ReferenceEquals(participant, spb));

            firstHost.Controls.Remove(panel);

            Assert.DoesNotContain(firstHost.GetContextHelpSnapshotParticipants(), participant => ReferenceEquals(participant, spb));

            secondHost.Controls.Add(panel);

            Assert.Contains(secondHost.GetContextHelpSnapshotParticipants(), participant => ReferenceEquals(participant, spb));
        });
    }

    [Fact]
    public void PrepareForContextHelpSnapshot_HidesRevealedTextAndPreservesPassword()
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tb = new TextBox
            {
                Location = new Point(16, 16),
                Width = 120,
                Height = 24
            };
            using var spb = new SecurePasswordBox(tb);
            spb.AddEyeToggle();
            _ = tb.Handle;

            SendMessage(tb.Handle, WM_CHAR, (IntPtr)'a', IntPtr.Zero);
            SendMessage(tb.Handle, WM_CHAR, (IntPtr)'b', IntPtr.Zero);
            Assert.IsType<Button>(Assert.Single(tb.Controls)).PerformClick();
            Assert.Equal("ab", tb.Text);
            Assert.Equal('\0', tb.PasswordChar);

            using var host = new ContextHelpForm
            {
                ClientSize = new Size(240, 160)
            };
            using var helpButton = new ContextHelpButton
            {
                Location = new Point(200, 8),
                Size = new Size(28, 28)
            };
            using var overlay = new ContextHelpOverlay
            {
                Dock = DockStyle.Fill
            };

            host.Controls.Add(tb);
            host.Controls.Add(helpButton);
            host.Controls.Add(overlay);
            StaTestHelper.CreateControlTree(host);

            var renderer = new ContextHelpSnapshotRenderer();
            using var snapshot = renderer.CaptureFormSnapshot(
                host,
                helpButton,
                overlay,
                host.GetContextHelpSnapshotParticipants());

            Assert.NotNull(snapshot);
            Assert.Equal(new string(BulletChar, 2), tb.Text);
            Assert.Equal(BulletChar, tb.PasswordChar);
            Assert.Equal("ab", ProtectedStringToString(spb.GetPassword()));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShortcutCutVariants_DoNotExposePlaintextOrDesyncDisplay(bool revealed)
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tb = new TextBox { Width = 100, Height = 24 };
            var clipboard = FakeSecurePasswordClipboardService.WithText(null);
            using var spb = new SecurePasswordBox(tb, clipboard);
            if (revealed)
                spb.AddEyeToggle();
            _ = tb.Handle;

            SendMessage(tb.Handle, WM_CHAR, (IntPtr)'a', IntPtr.Zero);
            SendMessage(tb.Handle, WM_CHAR, (IntPtr)'b', IntPtr.Zero);
            if (revealed)
                ((Button)tb.Controls[0]).PerformClick();
            tb.SelectionStart = 0;
            tb.SelectionLength = 2;

            Assert.True(spb.HandleShortcutKey(Keys.X, Keys.Control));
            Assert.True(spb.HandleShortcutKey(Keys.Delete, Keys.Shift));
            Assert.True(spb.HandleShortcutKey(Keys.Insert, Keys.Control));

            Assert.Equal(3, clipboard.SuppressPasswordClipboardWriteChecks);
            Assert.Equal("ab", ProtectedStringToString(spb.GetPassword()));
            Assert.Equal(revealed ? "ab" : new string(BulletChar, 2), tb.Text);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShortcutPasteVariants_UpdateBufferAndDisplayTogether(bool revealed)
    {
        StaTestHelper.RunOnSta(() =>
        {
            using var tb = new TextBox { Width = 100, Height = 24 };
            using var spb = new SecurePasswordBox(tb, FakeSecurePasswordClipboardService.WithText("xy"));
            if (revealed)
                spb.AddEyeToggle();
            _ = tb.Handle;

            SendMessage(tb.Handle, WM_CHAR, (IntPtr)'a', IntPtr.Zero);
            if (revealed)
                ((Button)tb.Controls[0]).PerformClick();
            tb.SelectionStart = 1;
            tb.SelectionLength = 0;

            Assert.True(spb.HandleShortcutKey(Keys.V, Keys.Control));
            Assert.Equal("axy", ProtectedStringToString(spb.GetPassword()));
            Assert.Equal(revealed ? "axy" : new string(BulletChar, 3), tb.Text);

            spb.Clear();
            if (revealed && tb.Controls.Count > 0 && tb.Text != string.Empty)
                ((Button)tb.Controls[0]).PerformClick();
            tb.SelectionStart = 0;
            tb.SelectionLength = 0;

            Assert.True(spb.HandleShortcutKey(Keys.Insert, Keys.Shift));
            Assert.Equal("xy", ProtectedStringToString(spb.GetPassword()));
            Assert.Equal(revealed ? "xy" : new string(BulletChar, 2), tb.Text);
        });
    }

    private static string ProtectedStringToString(ProtectedString value)
    {
        using (value)
        {
            return value.UseUnicodeSnapshot(snapshot =>
                Marshal.PtrToStringUni(snapshot.DangerousGetIntPtr(), snapshot.CharCount) ?? string.Empty);
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
