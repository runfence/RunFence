using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;
using RunFence.Core;

namespace RunFence.Infrastructure;

public sealed class ProcessIdentityReader(ILoggingService log) :
    IProcessQueryHandleProvider,
    IProcessPrivilegeStateReader,
    IProcessCreationTimeReader,
    IWindowProcessIdReader,
    IConsoleHostProcessResolver,
    IProcessOwnerSidReader,
    IProcessImagePathReader,
    IProcessAppContainerSidReader,
    IProcessIdentitySnapshotReader
{
    private const uint ProcessConsoleHostProcess = 49;
    private const int ErrorInsufficientBuffer = 122;
    private const int TokenElevation = 20;
    private const int TokenIntegrityLevel = 25;
    private const int TokenUser = 1;
    private const uint TokenQuery = 0x0008;

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

    public string? TryGetProcessAppContainerSid(uint processId) =>
        NativeTokenHelper.TryGetProcessAppContainerSid(processId)?.Value;

    public bool TryOpenProcessForQuery(uint processId, out SafeProcessHandle processHandle)
    {
        processHandle = ProcessNative.OpenProcess(ProcessNative.ProcessQueryLimitedInformation, false, processId);
        return !processHandle.IsInvalid;
    }

    public bool TryGetProcessElevation(uint processId, out bool isElevated)
    {
        isElevated = false;
        if (!TryOpenProcessForQuery(processId, out var processHandle))
            return false;

        using (processHandle)
        {
            return TryReadTokenElevation(processHandle.DangerousGetHandle(), out isElevated);
        }
    }

    public bool TryGetProcessIntegrityLevel(uint processId, out int integrityLevel)
    {
        if (!TryOpenProcessForQuery(processId, out var processHandle))
        {
            integrityLevel = 0;
            return false;
        }

        using (processHandle)
        {
            return TryReadProcessIntegrityLevel(processHandle.DangerousGetHandle(), out integrityLevel);
        }
    }

    public bool TryGetProcessCreationTimeUtcTicks(uint processId, out long creationTimeUtcTicks)
    {
        creationTimeUtcTicks = 0;
        if (!TryOpenProcessForQuery(processId, out var processHandle))
            return false;

        using (processHandle)
        {
            if (!ProcessNative.GetProcessTimes(
                    processHandle.DangerousGetHandle(),
                    out var creationTime,
                    out _,
                    out _,
                    out _))
                return false;

            creationTimeUtcTicks = DateTime.FromFileTimeUtc(creationTime.ToLong()).Ticks;
            return true;
        }
    }

    public string? TryGetProcessImagePath(uint processId)
    {
        if (!TryOpenProcessForQuery(processId, out var processHandle))
            return null;

        using (processHandle)
        {
            return TryReadProcessImagePath(processHandle);
        }
    }

    public ProcessIdentitySnapshot? TryReadProcessIdentity(uint processId)
    {
        if (!TryOpenProcessForQuery(processId, out var processHandle))
            return null;

        using (processHandle)
        {
            var imagePath = TryReadProcessImagePath(processHandle);
            var ownerSid = default(string);
            int? integrityLevel = null;
            if (TryOpenProcessTokenForQuery(processHandle.DangerousGetHandle(), out var tokenHandle))
            {
                try
                {
                    ownerSid = TryReadTokenSid(tokenHandle, TokenUser);
                    integrityLevel = TryReadIntegrityLevel(tokenHandle, out var level)
                        ? level
                        : null;
                }
                finally
                {
                    ProcessNative.CloseHandle(tokenHandle);
                }
            }

            return new ProcessIdentitySnapshot(imagePath, ownerSid, integrityLevel);
        }
    }

    private static string? TryReadProcessImagePath(SafeProcessHandle processHandle)
    {
        int capacity = 260;
        while (capacity <= 32768)
        {
            var imagePath = new StringBuilder(capacity);
            uint imagePathLength = (uint)imagePath.Capacity;
            if (ProcessNative.QueryFullProcessImageName(processHandle, 0, imagePath, ref imagePathLength))
                return imagePath.ToString();

            if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer)
                return null;

            capacity = capacity == 32768 ? 32769 : Math.Min(capacity * 2, 32768);
        }

        return null;
    }

    private static bool TryReadProcessIntegrityLevel(IntPtr processHandle, out int integrityLevel)
    {
        integrityLevel = 0;
        if (!TryOpenProcessTokenForQuery(processHandle, out var tokenHandle))
            return false;

        try
        {
            return TryReadIntegrityLevel(tokenHandle, out integrityLevel);
        }
        finally
        {
            ProcessNative.CloseHandle(tokenHandle);
        }
    }

    private static bool TryOpenProcessTokenForQuery(IntPtr processHandle, out IntPtr tokenHandle) =>
        ProcessNative.OpenProcessToken(processHandle, TokenQuery, out tokenHandle);

    private static bool TryReadTokenElevation(IntPtr processHandle, out bool isElevated)
    {
        isElevated = false;
        if (!ProcessNative.OpenProcessToken(processHandle, TokenQuery, out var tokenHandle))
            return false;

        try
        {
            var buffer = Marshal.AllocHGlobal(sizeof(int));
            try
            {
                if (!ProcessNative.GetTokenInformation(tokenHandle, TokenElevation, buffer, sizeof(int), out _))
                    return false;

                isElevated = Marshal.ReadInt32(buffer) != 0;
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            ProcessNative.CloseHandle(tokenHandle);
        }
    }

    private static string? TryReadTokenSid(IntPtr tokenHandle, int tokenInformationClass)
    {
        ProcessNative.GetTokenInformation(tokenHandle, tokenInformationClass, IntPtr.Zero, 0, out var needed);
        if (needed <= 0)
            return null;

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!ProcessNative.GetTokenInformation(tokenHandle, tokenInformationClass, buffer, needed, out _))
                return null;

            var sidPtr = Marshal.ReadIntPtr(buffer);
            if (sidPtr == IntPtr.Zero)
                return null;

            try
            {
                return new SecurityIdentifier(sidPtr).Value;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryReadIntegrityLevel(IntPtr tokenHandle, out int integrityLevel)
    {
        integrityLevel = 0;
        ProcessNative.GetTokenInformation(tokenHandle, TokenIntegrityLevel, IntPtr.Zero, 0, out var needed);
        if (needed <= 0)
            return false;

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!ProcessNative.GetTokenInformation(tokenHandle, TokenIntegrityLevel, buffer, needed, out _))
                return false;

            var sidPtr = Marshal.ReadIntPtr(buffer);
            if (sidPtr == IntPtr.Zero)
                return false;

            SecurityIdentifier sid;
            try
            {
                sid = new SecurityIdentifier(sidPtr);
            }
            catch (ArgumentException)
            {
                return false;
            }

            var binary = new byte[sid.BinaryLength];
            sid.GetBinaryForm(binary, 0);
            integrityLevel = binary[^4]
                             | (binary[^3] << 8)
                             | (binary[^2] << 16)
                             | (binary[^1] << 24);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
