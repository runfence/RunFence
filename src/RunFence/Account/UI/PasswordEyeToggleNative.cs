using System.Runtime.InteropServices;

namespace RunFence.Account.UI;

internal static class PasswordEyeToggleNative
{
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
