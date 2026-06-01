using System.Runtime.InteropServices;

namespace RunFence.Infrastructure;

internal static class SystemHandleNative
{
    public const int SystemExtendedHandleInformation = 64;
    public const int ObjectTypeInformation = 2;

    public const int StatusSuccess = 0;
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    public const int StatusBufferOverflow = unchecked((int)0x80000005);
    public const int StatusBufferTooSmall = unchecked((int)0xC0000023);

    public const uint DuplicateSameAccess = 0x00000002;

    [DllImport("ntdll.dll")]
    public static extern int NtQuerySystemInformation(
        int systemInformationClass,
        IntPtr systemInformation,
        int systemInformationLength,
        out int returnLength);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryObject(
        IntPtr handle,
        int objectInformationClass,
        IntPtr objectInformation,
        int objectInformationLength,
        out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct SystemHandleTableEntryInfoEx
    {
        public readonly IntPtr Object;
        public readonly IntPtr UniqueProcessId;
        public readonly IntPtr HandleValue;
        public readonly uint GrantedAccess;
        public readonly ushort CreatorBackTraceIndex;
        public readonly ushort ObjectTypeIndex;
        public readonly uint HandleAttributes;
        public readonly uint Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct UnicodeString
    {
        public readonly ushort Length;
        public readonly ushort MaximumLength;
        public readonly IntPtr Buffer;
    }
}
