using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RunFence.Launching.Windows;

public static class ProcessInspectionNative
{
    public const uint ProcessQueryLimitedInformation = 0x1000;

    private const uint TokenQuery = 0x0008;
    private const int TokenUser = 1;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern SafeProcessHandle OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(
        IntPtr hProcess,
        uint dwFlags,
        StringBuilder lpExeName,
        ref uint lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessTimes(
        IntPtr hProcess,
        out FileTimeStruct creationTime,
        out FileTimeStruct exitTime,
        out FileTimeStruct kernelTime,
        out FileTimeStruct userTime);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(
        IntPtr TokenHandle,
        int TokenInformationClass,
        IntPtr TokenInformation,
        uint TokenInformationLength,
        out uint ReturnLength);

    [StructLayout(LayoutKind.Sequential)]
    public struct FileTimeStruct
    {
        public uint DwLowDateTime;
        public uint DwHighDateTime;

        public long ToLong() => ((long)DwHighDateTime << 32) | DwLowDateTime;
    }

    public sealed class SafeNativeProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeNativeProcessHandle(IntPtr handle) : base(ownsHandle: true)
        {
            SetHandle(handle);
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    public static string? GetTokenUserSid(IntPtr processHandle)
    {
        if (!OpenProcessToken(processHandle, TokenQuery, out var tokenHandle))
            return null;

        try
        {
            GetTokenInformation(tokenHandle, TokenUser, IntPtr.Zero, 0, out var needed);
            if (needed == 0)
                return null;

            var buffer = Marshal.AllocHGlobal((int)needed);
            try
            {
                if (!GetTokenInformation(tokenHandle, TokenUser, buffer, needed, out _))
                    return null;

                var sidPtr = Marshal.ReadIntPtr(buffer);
                return sidPtr == IntPtr.Zero ? null : new SecurityIdentifier(sidPtr).Value;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            CloseHandle(tokenHandle);
        }
    }
}
