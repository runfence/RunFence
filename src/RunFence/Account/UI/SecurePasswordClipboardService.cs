using RunFence.Infrastructure;

namespace RunFence.Account.UI;

internal sealed class SecurePasswordClipboardService : ISecurePasswordClipboardService
{
    private const int CF_UNICODETEXT = 13;

    public ISecurePasswordPasteSession? OpenUnicodeText(out bool unicodeTextAvailable)
    {
        unicodeTextAvailable = SecurePasswordClipboardNative.IsClipboardFormatAvailable(CF_UNICODETEXT);
        if (!unicodeTextAvailable)
            return null;

        if (!ClipboardNative.OpenClipboard(IntPtr.Zero))
        {
            unicodeTextAvailable = false;
            return null;
        }

        IntPtr hData = IntPtr.Zero;
        IntPtr pData = IntPtr.Zero;
        bool closeClipboard = true;
        try
        {
            hData = ClipboardNative.GetClipboardData(CF_UNICODETEXT);
            if (hData == IntPtr.Zero)
            {
                unicodeTextAvailable = false;
                return null;
            }
            else
            {
                pData = ClipboardNative.GlobalLock(hData);
                if (pData == IntPtr.Zero)
                {
                    unicodeTextAvailable = false;
                    return null;
                }
                else
                {
                    var clipboardText = new SecurePasswordClipboardText(hData, pData);
                    pData = IntPtr.Zero;
                    closeClipboard = false;
                    return clipboardText;
                }
            }
        }
        finally
        {
            if (pData != IntPtr.Zero)
                ClipboardNative.GlobalUnlock(hData);
            if (closeClipboard)
                ClipboardNative.CloseClipboard();
        }
    }

    public bool ShouldSuppressPasswordClipboardWrite() =>
        true;
}
