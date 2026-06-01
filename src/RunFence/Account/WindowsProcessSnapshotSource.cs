using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using RunFence.Infrastructure;
using RunFence.Launching.Processes;

namespace RunFence.Account;

public sealed class WindowsProcessSnapshotSource(IProcessSnapshotEnumerator processEnumerator) : IProcessSnapshotSource
{
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;

    public IReadOnlyList<int> GetProcessIds()
    {
        return processEnumerator.GetProcesses().Select(process => process.ProcessId).ToList();
    }

    public string? GetTokenSid(int pid, int tokenInfoClass)
    {
        using var handle = OpenLimitedProcess(pid);
        return handle.IsInvalid ? null : ProcessNative.GetTokenSid(handle.DangerousGetHandle(), tokenInfoClass);
    }

    public string? GetAppContainerSid(int pid) =>
        GetTokenSid(pid, ProcessNative.TokenAppContainerSid);

    public bool HasExited(int pid)
    {
        using var handle = OpenLimitedProcess(pid);
        return !handle.IsInvalid && HasExited(handle.DangerousGetHandle());
    }

    public ProcessInfo? ReadProcessInfo(int pid)
    {
        using var limitedHandle = OpenLimitedProcess(pid);
        if (limitedHandle.IsInvalid)
            return null;

        string? executablePath;
        string? commandLine = null;

        using var fullHandle = new ProcessNative.SafeNativeProcessHandle(
            ProcessNative.OpenProcess(ProcessQueryInformation | ProcessVmRead, false, pid));
        if (!fullHandle.IsInvalid)
        {
            executablePath = GetExecutablePath(fullHandle.DangerousGetHandle());
            commandLine = GetCommandLine(fullHandle.DangerousGetHandle());
        }
        else
        {
            executablePath = GetExecutablePath(limitedHandle.DangerousGetHandle());
        }

        long? startTimeUtcTicks = null;
        if (ProcessNative.GetProcessTimes(
                limitedHandle.DangerousGetHandle(),
                out var creationTime,
                out _,
                out _,
                out _))
        {
            startTimeUtcTicks = DateTime.FromFileTimeUtc(creationTime.ToLong()).Ticks;
        }

        return new ProcessInfo(pid, executablePath, commandLine, startTimeUtcTicks);
    }

    private static ProcessNative.SafeNativeProcessHandle OpenLimitedProcess(int pid) =>
        new(ProcessNative.OpenProcess(ProcessNative.ProcessQueryLimitedInformation, false, pid));

    private static bool HasExited(IntPtr processHandle)
    {
        if (!ProcessNative.GetProcessTimes(
                processHandle,
                out _,
                out var exitTime,
                out _,
                out _))
        {
            return false;
        }

        return exitTime.ToLong() != 0;
    }

    private static string? GetExecutablePath(IntPtr processHandle)
    {
        var buffer = new StringBuilder(1024);
        uint size = (uint)buffer.Capacity;
        return ProcessNative.QueryFullProcessImageName(processHandle, 0, buffer, ref size) ? buffer.ToString() : null;
    }

    private static string? GetCommandLine(IntPtr processHandle)
    {
        if (ProcessNative.IsWow64Process(processHandle, out bool isWow64) && isWow64)
            return null;

        var processBasicInformation = new ProcessNative.ProcessBasicInformation();
        if (ProcessNative.NtQueryInformationProcess(
                processHandle,
                0,
                ref processBasicInformation,
                (uint)Marshal.SizeOf<ProcessNative.ProcessBasicInformation>(),
                out _) != 0)
        {
            return null;
        }

        if (processBasicInformation.PebBaseAddress == IntPtr.Zero)
            return null;

        IntPtr processParameters = ReadPointer(processHandle, processBasicInformation.PebBaseAddress + 0x20);
        if (processParameters == IntPtr.Zero)
            return null;

        ushort commandLineLength = ReadUShort(processHandle, processParameters + 0x70);
        IntPtr commandLineBuffer = ReadPointer(processHandle, processParameters + 0x78);
        if (commandLineLength == 0 || commandLineBuffer == IntPtr.Zero)
            return null;

        var buffer = Marshal.AllocHGlobal(commandLineLength);
        try
        {
            if (!ProcessNative.ReadProcessMemory(processHandle, commandLineBuffer, buffer, commandLineLength, out var bytesRead))
                return null;

            var bytes = new byte[(int)bytesRead];
            Marshal.Copy(buffer, bytes, 0, (int)bytesRead);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static IntPtr ReadPointer(IntPtr processHandle, IntPtr address)
    {
        var buffer = Marshal.AllocHGlobal(IntPtr.Size);
        try
        {
            return ProcessNative.ReadProcessMemory(processHandle, address, buffer, IntPtr.Size, out _)
                ? Marshal.ReadIntPtr(buffer)
                : IntPtr.Zero;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ushort ReadUShort(IntPtr processHandle, IntPtr address)
    {
        var buffer = Marshal.AllocHGlobal(2);
        try
        {
            return ProcessNative.ReadProcessMemory(processHandle, address, buffer, 2, out _)
                ? (ushort)Marshal.ReadInt16(buffer)
                : (ushort)0;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
