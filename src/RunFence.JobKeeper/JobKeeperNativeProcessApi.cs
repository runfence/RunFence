using System.Runtime.InteropServices;
using System.Text;
using RunFence.Core;

namespace RunFence.JobKeeper;

public sealed class JobKeeperNativeProcessApi : IJobKeeperNativeProcessApi, IJobKeeperEnvironmentNativeApi
{
    private const uint StartfUseShowWindow = 0x00000001;
    private const uint StartfForceOffFeedback = 0x00000080;
    private const ushort SwShownormal = 1;
    private const uint TokenDuplicate = 0x0002;
    private const uint TokenQuery = 0x0008;
    private const int ErrorInsufficientBuffer = 122;

    public bool CreateProcess(
        string? applicationName,
        StringBuilder commandLine,
        uint creationFlags,
        IntPtr environmentBlock,
        string? workingDirectory,
        bool hideWindow,
        bool suppressStartupFeedback,
        out JobKeeperProcessInformation processInformation)
    {
        var si = new STARTUPINFO
        {
            cb = Marshal.SizeOf<STARTUPINFO>(),
            lpDesktop = @"WinSta0\Default",
            dwFlags = StartfUseShowWindow | (suppressStartupFeedback ? StartfForceOffFeedback : 0u),
            wShowWindow = hideWindow ? (ushort)0 : SwShownormal
        };

        var result = CreateProcess(
            applicationName,
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

    public bool TerminateProcess(IntPtr processHandle, uint exitCode) =>
        TerminateNativeProcess(processHandle, exitCode);

    public string? TryGetProcessImagePath(IntPtr processHandle)
    {
        int capacity = 260;
        while (capacity <= 32768)
        {
            var imagePath = new StringBuilder(capacity);
            uint imagePathLength = (uint)imagePath.Capacity;
            if (QueryFullProcessImageName(processHandle, 0, imagePath, ref imagePathLength))
                return imagePath.ToString();

            if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
                return null;

            capacity = capacity == 32768 ? 32769 : Math.Min(capacity * 2, 32768);
        }

        return null;
    }

    public bool WaitForProcessExit(IntPtr processHandle, uint timeoutMilliseconds) =>
        WaitForSingleObject(processHandle, timeoutMilliseconds) == 0;

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

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateNativeProcess(IntPtr hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(
        IntPtr hProcess,
        uint dwFlags,
        StringBuilder lpExeName,
        ref uint lpdwSize);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("userenv.dll", EntryPoint = "CreateEnvironmentBlock", SetLastError = true)]
    private static extern bool CreateNativeEnvironmentBlock(out IntPtr environmentBlock, IntPtr tokenHandle, bool inherit);

    [DllImport("userenv.dll", EntryPoint = "DestroyEnvironmentBlock", SetLastError = true)]
    private static extern bool DestroyNativeEnvironmentBlock(IntPtr environmentBlock);
}
