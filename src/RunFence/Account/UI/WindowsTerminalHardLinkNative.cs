using System.ComponentModel;
using System.Runtime.InteropServices;

namespace RunFence.Account.UI;

internal static class WindowsTerminalHardLinkNative
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    public static void CreateFileHardLink(string linkPath, string existingFilePath)
    {
        if (CreateHardLink(linkPath, existingFilePath, IntPtr.Zero))
            return;

        throw new IOException(
            $"Failed to create hard link '{linkPath}' for '{existingFilePath}'.",
            new Win32Exception(Marshal.GetLastWin32Error()));
    }
}
