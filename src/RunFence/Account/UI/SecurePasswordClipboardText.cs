using System.Runtime.InteropServices;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

internal sealed class SecurePasswordClipboardText : ISecurePasswordPasteSession
{
    private readonly IntPtr _hData;
    private readonly IntPtr _pData;
    private bool _disposed;

    internal SecurePasswordClipboardText(IntPtr hData, IntPtr pData)
    {
        _hData = hData;
        _pData = pData;
    }

    public char ReadChar(int charIndex)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return (char)Marshal.ReadInt16(_pData, charIndex * 2);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        try
        {
            ClipboardNative.GlobalUnlock(_hData);
        }
        finally
        {
            ClipboardNative.CloseClipboard();
        }
    }
}
