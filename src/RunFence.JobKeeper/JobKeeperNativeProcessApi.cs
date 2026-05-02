using System.Runtime.InteropServices;
using System.Text;

namespace RunFence.JobKeeper;

public sealed class JobKeeperNativeProcessApi : IJobKeeperNativeProcessApi, IJobKeeperEnvironmentNativeApi
{
    private const uint StartfUseShowWindow = 0x00000001;
    private const ushort SwShownormal = 1;
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenQuery = 0x0008;

    public bool CreateProcess(
        StringBuilder commandLine,
        uint creationFlags,
        IntPtr environmentBlock,
        string? workingDirectory,
        bool hideWindow,
        out JobKeeperProcessInformation processInformation)
    {
        var si = new STARTUPINFO
        {
            cb = Marshal.SizeOf<STARTUPINFO>(),
            lpDesktop = @"WinSta0\Default",
            dwFlags = StartfUseShowWindow,
            wShowWindow = hideWindow ? (ushort)0 : SwShownormal
        };

        var result = CreateProcess(
            null,
            commandLine,
            IntPtr.Zero,
            IntPtr.Zero,
            false,
            creationFlags,
            environmentBlock,
            workingDirectory,
            ref si,
            out var nativeProcessInformation);

        processInformation = new JobKeeperProcessInformation(
            nativeProcessInformation.hProcess,
            nativeProcessInformation.hThread,
            nativeProcessInformation.dwProcessId,
            nativeProcessInformation.dwThreadId);
        return result;
    }

    public void CloseHandle(IntPtr handle) => CloseNativeHandle(handle);

    public bool OpenCurrentProcessToken(out IntPtr tokenHandle) =>
        OpenProcessToken(GetCurrentProcess(), TokenQuery | TokenDuplicate, out tokenHandle);

    public bool CreateEnvironmentBlock(out IntPtr environmentBlock, IntPtr tokenHandle) =>
        CreateNativeEnvironmentBlock(out environmentBlock, tokenHandle, false);

    public bool DestroyEnvironmentBlock(IntPtr environmentBlock) => DestroyNativeEnvironmentBlock(environmentBlock);

    public int GetLastWin32Error() => Marshal.GetLastWin32Error();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize;
        public uint dwXCountChars, dwYCountChars, dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow, cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public uint dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        StringBuilder lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    private static extern bool CloseNativeHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("userenv.dll", EntryPoint = "CreateEnvironmentBlock", SetLastError = true)]
    private static extern bool CreateNativeEnvironmentBlock(out IntPtr environmentBlock, IntPtr tokenHandle, bool inherit);

    [DllImport("userenv.dll", EntryPoint = "DestroyEnvironmentBlock", SetLastError = true)]
    private static extern bool DestroyNativeEnvironmentBlock(IntPtr environmentBlock);
}
