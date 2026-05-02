using System.Runtime.InteropServices;

namespace RunFence.Account.UI;

internal static class SecurePasswordClipboardNative
{
    [DllImport("user32.dll")]
    public static extern bool IsClipboardFormatAvailable(uint format);
}
