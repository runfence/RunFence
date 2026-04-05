using System.Runtime.InteropServices;

namespace RunFence.Account;

internal static class ProfileRepairNative
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public string Buffer;
    }

    [DllImport("ntdll.dll", CharSet = CharSet.Unicode)]
    public static extern int NtRenameKey(IntPtr KeyHandle, ref UNICODE_STRING NewName);
}