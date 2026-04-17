using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using Timer = System.Windows.Forms.Timer;

namespace RunFence.Account.UI;

/// <summary>
/// Manages secure clipboard operations: copies a <see cref="SecureString"/> to the clipboard
/// while excluding it from clipboard monitoring tools, and automatically clears it after 60 seconds
/// if the clipboard contents have not changed.
/// </summary>
public class SecureClipboardService : IDisposable
{
    private Timer? _clipboardClearTimer;
    private byte[]? _clipboardExpectedHash;

    public void CopySecureStringToClipboard(SecureString password)
    {
        var ptr = Marshal.SecureStringToGlobalAllocUnicode(password);
        try
        {
            // PtrToStringUni creates a managed string that cannot be zeroed after use — known
            // limitation of placing text on the clipboard. Mitigated by the 60-second clear timer.
            var text = Marshal.PtrToStringUni(ptr)!;
            var dataObject = new DataObject(DataFormats.UnicodeText, text);
            dataObject.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
            Clipboard.SetDataObject(dataObject, copy: true);
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(ptr);
        }
    }

    public void ScheduleClipboardClear()
    {
        _clipboardClearTimer?.Dispose();
        _clipboardClearTimer = null;

        _clipboardExpectedHash = SHA256.HashData(Encoding.Unicode.GetBytes(Clipboard.GetText()));

        _clipboardClearTimer = new Timer { Interval = 60_000 };
        _clipboardClearTimer.Tick += OnClipboardClearTimerTick;
        _clipboardClearTimer.Start();
    }

    private void OnClipboardClearTimerTick(object? sender, EventArgs e)
    {
        if (Clipboard.ContainsText() && _clipboardExpectedHash != null &&
            SHA256.HashData(Encoding.Unicode.GetBytes(Clipboard.GetText()))
                .AsSpan().SequenceEqual(_clipboardExpectedHash))
            Clipboard.Clear();

        _clipboardExpectedHash = null;
        _clipboardClearTimer?.Dispose();
        _clipboardClearTimer = null;
    }

    public void Dispose()
    {
        _clipboardClearTimer?.Dispose();
        _clipboardClearTimer = null;
    }
}
