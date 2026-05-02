using System.Runtime.InteropServices;

namespace RunFence.Infrastructure;

internal static class LocalSamSidResolverNative
{
    public const int NerrSuccess = 0;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ConvertSidToStringSidW(IntPtr pSid, out IntPtr stringSid);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct USER_INFO_23
    {
        public IntPtr usri23_name;
        public IntPtr usri23_full_name;
        public IntPtr usri23_comment;
        public uint usri23_flags;
        public IntPtr usri23_user_sid;
    }
}
