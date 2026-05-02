using System.Runtime.InteropServices;
using System.Text;
using RunFence.Core;

namespace RunFence.Infrastructure;

public sealed class ProcessIdentityReader(ILoggingService log) : IProcessIdentityReader
{
    private const uint ProcessConsoleHostProcess = 49;
    private const int ErrorInsufficientBuffer = 122;

    public uint GetWindowProcessId(IntPtr hWnd)
    {
        WindowNative.GetWindowThreadProcessId(hWnd, out uint processId);
        return processId;
    }

    public bool TryGetConsoleHostProcessId(int processId, out int consoleHostProcessId)
    {
        consoleHostProcessId = 0;
        IntPtr hProcess = ProcessNative.OpenProcess(ProcessNative.PROCESS_QUERY_INFORMATION, false, processId);
        if (hProcess == IntPtr.Zero)
        {
            log.Warn($"ClipboardPasteInterceptService: OpenProcess({processId}) for conhost lookup failed with error {Marshal.GetLastWin32Error()}.");
            return false;
        }

        try
        {
            uint status = ProcessNative.NtQueryInformationProcess(
                hProcess,
                ProcessConsoleHostProcess,
                out IntPtr conhostPid,
                (uint)IntPtr.Size,
                out _);

            if (status != 0)
            {
                log.Warn($"ClipboardPasteInterceptService: ProcessConsoleHostProcess query for pid {processId} failed with NTSTATUS 0x{status:X8}.");
                return false;
            }

            consoleHostProcessId = (int)conhostPid.ToInt64();
            return consoleHostProcessId != 0;
        }
        finally
        {
            ProcessNative.CloseHandle(hProcess);
        }
    }

    public string? TryGetProcessOwnerSid(uint processId) =>
        NativeTokenHelper.TryGetProcessOwnerSid(processId)?.Value;

    public string? TryGetProcessImageFileName(uint processId)
    {
        IntPtr hProcess = ProcessNative.OpenProcess(ProcessNative.ProcessQueryLimitedInformation, false, (int)processId);
        if (hProcess == IntPtr.Zero)
            return null;

        try
        {
            int capacity = 260;
            while (capacity <= 32768)
            {
                var imagePath = new StringBuilder(capacity);
                uint imagePathLength = (uint)imagePath.Capacity;
                if (ProcessNative.QueryFullProcessImageName(hProcess, 0, imagePath, ref imagePathLength))
                    return Path.GetFileName(imagePath.ToString());

                if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
                    return null;

                capacity = capacity == 32768 ? 32769 : Math.Min(capacity * 2, 32768);
            }

            return null;
        }
        finally
        {
            ProcessNative.CloseHandle(hProcess);
        }
    }
}
