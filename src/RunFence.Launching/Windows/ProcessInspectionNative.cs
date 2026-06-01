using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace RunFence.Launching.Windows;

public static class ProcessInspectionNative
{
    public const int SystemProcessInformation = 5;
    public const int StatusSuccess = 0;
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    public const int StatusBufferOverflow = unchecked((int)0x80000005);
    public const int StatusBufferTooSmall = unchecked((int)0xC0000023);
    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const int MandatoryLevelLow = 0x1000;
    public const int MandatoryLevelMedium = 0x2000;
    public const int ErrorAccessDenied = 5;
    public const int ErrorInvalidParameter = 87;

    public const uint TokenQuery = 0x0008;
    public const int TokenUser = 1;
    public const int TokenIntegrityLevel = 25;

    [DllImport("ntdll.dll")]
    public static extern int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out int returnLength);

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

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct SystemProcessInformationEntry
    {
        public readonly uint NextEntryOffset;
        public readonly uint NumberOfThreads;
        public readonly long WorkingSetPrivateSize;
        public readonly uint HardFaultCount;
        public readonly uint NumberOfThreadsHighWatermark;
        public readonly ulong CycleTime;
        public readonly long CreateTime;
        public readonly long UserTime;
        public readonly long KernelTime;
        public readonly UnicodeString ImageName;
        public readonly int BasePriority;
        public readonly IntPtr UniqueProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct UnicodeString
    {
        public readonly ushort Length;
        public readonly ushort MaximumLength;
        public readonly IntPtr Buffer;
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
