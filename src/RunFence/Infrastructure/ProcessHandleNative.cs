using System.Runtime.InteropServices;

namespace RunFence.Infrastructure;

internal static class ProcessHandleNative
{
    public const int ProcessHandleInformation = 51;

    [DllImport("ntdll.dll")]
    public static extern int NtQueryInformationProcess(
        IntPtr processHandle,
        int processInformationClass,
        IntPtr processInformation,
        int processInformationLength,
        out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct PROCESS_HANDLE_SNAPSHOT_INFORMATION
    {
        public readonly IntPtr NumberOfHandles;
        public readonly IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct PROCESS_HANDLE_TABLE_ENTRY_INFO
    {
        public readonly IntPtr HandleValue;
        public readonly UIntPtr HandleCount;
        public readonly UIntPtr PointerCount;
        public readonly uint GrantedAccess;
        public readonly uint ObjectTypeIndex;
        public readonly uint HandleAttributes;
        public readonly uint Reserved;
    }
}
