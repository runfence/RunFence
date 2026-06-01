using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using RunFence.Launching.Windows;

namespace RunFence.Launching.Processes;

public sealed class ProcessSnapshotScanner :
    IProcessSnapshotEnumerator,
    IProcessImageNameSnapshotReader,
    IProcessExecutablePathReader,
    IProcessOwnerInfoReader,
    IProcessIntegrityLevelReader
{
    private const int ImagePathBufferLength = 32767;

    public IReadOnlyList<ProcessSnapshotEntry> GetProcesses()
    {
        var result = new List<ProcessSnapshotEntry>();
        using var processBuffer = QueryProcessBuffer();
        var entryAddress = processBuffer.DangerousGetHandle();
        while (entryAddress != IntPtr.Zero)
        {
            var entry = Marshal.PtrToStructure<ProcessInspectionNative.SystemProcessInformationEntry>(entryAddress);
            if (entry.UniqueProcessId.ToInt64() > 0)
            {
                result.Add(new ProcessSnapshotEntry(
                    entry.UniqueProcessId.ToInt32(),
                    entry.CreateTime > 0 ? DateTime.FromFileTimeUtc(entry.CreateTime).Ticks : null));
            }

            if (entry.NextEntryOffset == 0)
                break;

            entryAddress = IntPtr.Add(entryAddress, (int)entry.NextEntryOffset);
        }

        return result;
    }

    public IReadOnlyList<LightweightProcessInfo> GetProcessesByImageName(string imageName)
    {
        if (string.IsNullOrWhiteSpace(imageName))
            return [];

        return GetProcessesByImageNameCore(imageName);
    }

    public string? GetExecutablePath(int processId)
    {
        using var processHandle = ProcessInspectionNative.OpenProcess(
            ProcessInspectionNative.ProcessQueryLimitedInformation,
            false,
            (uint)processId);
        if (processHandle.IsInvalid)
            return null;

        var buffer = new StringBuilder(ImagePathBufferLength);
        uint size = (uint)buffer.Capacity;
        return ProcessInspectionNative.QueryFullProcessImageName(
            processHandle.DangerousGetHandle(),
            0,
            buffer,
            ref size)
            ? buffer.ToString()
            : null;
    }

    public ProcessOwnerInfo GetProcessOwner(int processId, string expectedOwnerSid)
    {
        using var processHandle = ProcessInspectionNative.OpenProcess(
            ProcessInspectionNative.ProcessQueryLimitedInformation,
            false,
            (uint)processId);
        if (processHandle.IsInvalid)
            return OwnerFromOpenFailure(Marshal.GetLastWin32Error());

        if (!ProcessInspectionNative.OpenProcessToken(
                processHandle.DangerousGetHandle(),
                ProcessInspectionNative.TokenQuery,
                out var tokenHandle))
        {
            return OwnerFromOpenFailure(Marshal.GetLastWin32Error());
        }

        try
        {
            return ReadTokenOwner(tokenHandle, expectedOwnerSid);
        }
        finally
        {
            ProcessInspectionNative.CloseHandle(tokenHandle);
        }
    }

    public int? GetIntegrityLevel(int processId)
    {
        using var processHandle = ProcessInspectionNative.OpenProcess(
            ProcessInspectionNative.ProcessQueryLimitedInformation,
            false,
            (uint)processId);
        if (processHandle.IsInvalid)
            return null;

        if (!ProcessInspectionNative.OpenProcessToken(
                processHandle.DangerousGetHandle(),
                ProcessInspectionNative.TokenQuery,
                out var tokenHandle))
        {
            return null;
        }

        try
        {
            return ReadTokenIntegrityLevel(tokenHandle);
        }
        finally
        {
            ProcessInspectionNative.CloseHandle(tokenHandle);
        }
    }

    private static IReadOnlyList<LightweightProcessInfo> GetProcessesByImageNameCore(string imageName)
    {
        var result = new List<LightweightProcessInfo>();
        using var processBuffer = QueryProcessBuffer();
        var entryAddress = processBuffer.DangerousGetHandle();
        while (entryAddress != IntPtr.Zero)
        {
            var entry = Marshal.PtrToStructure<ProcessInspectionNative.SystemProcessInformationEntry>(entryAddress);
            var processImageName = GetImageName(entry.ImageName, imageName);
            if (processImageName != null && entry.UniqueProcessId.ToInt64() > 0)
            {
                result.Add(new LightweightProcessInfo(
                    entry.UniqueProcessId.ToInt32(),
                    processImageName,
                    entry.CreateTime > 0 ? DateTime.FromFileTimeUtc(entry.CreateTime).Ticks : null));
            }

            if (entry.NextEntryOffset == 0)
                break;

            entryAddress = IntPtr.Add(entryAddress, (int)entry.NextEntryOffset);
        }

        return result;
    }

    private static ProcessInformationBuffer QueryProcessBuffer()
    {
        var bufferLength = 64 * 1024;
        while (true)
        {
            var buffer = new ProcessInformationBuffer(bufferLength);
            var status = ProcessInspectionNative.NtQuerySystemInformation(
                ProcessInspectionNative.SystemProcessInformation,
                buffer.DangerousGetHandle(),
                bufferLength,
                out var returnLength);

            if (status == ProcessInspectionNative.StatusSuccess)
                return buffer;

            buffer.Dispose();
            if (status is not (ProcessInspectionNative.StatusInfoLengthMismatch
                or ProcessInspectionNative.StatusBufferOverflow
                or ProcessInspectionNative.StatusBufferTooSmall))
            {
                throw new InvalidOperationException($"NtQuerySystemInformation failed with status 0x{status:X8}.");
            }

            bufferLength = returnLength > bufferLength ? returnLength : bufferLength * 2;
        }
    }

    private static string? GetImageName(ProcessInspectionNative.UnicodeString imageName, string expectedImageName)
    {
        if (imageName.Length == 0 || imageName.Buffer == IntPtr.Zero)
            return null;

        if (imageName.Length != expectedImageName.Length * sizeof(char))
            return null;

        var processImageName = Marshal.PtrToStringUni(imageName.Buffer, imageName.Length / sizeof(char));
        if (processImageName == null)
            return null;

        if (!string.Equals(processImageName, expectedImageName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return processImageName;
    }

    private static ProcessOwnerInfo ReadTokenOwner(IntPtr tokenHandle, string expectedOwnerSid)
    {
        ProcessInspectionNative.GetTokenInformation(
            tokenHandle,
            ProcessInspectionNative.TokenUser,
            IntPtr.Zero,
            0,
            out var needed);
        if (needed == 0)
            return OwnerFromOpenFailure(Marshal.GetLastWin32Error());

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!ProcessInspectionNative.GetTokenInformation(
                    tokenHandle,
                    ProcessInspectionNative.TokenUser,
                    buffer,
                    needed,
                    out _))
            {
                return OwnerFromOpenFailure(Marshal.GetLastWin32Error());
            }

            var sidPtr = Marshal.ReadIntPtr(buffer);
            if (sidPtr == IntPtr.Zero)
                return new ProcessOwnerInfo(ProcessOwnerMatch.Unknown, null);

            var sid = new SecurityIdentifier(sidPtr).Value;
            var match = string.Equals(sid, expectedOwnerSid, StringComparison.OrdinalIgnoreCase)
                ? ProcessOwnerMatch.ExpectedOwner
                : ProcessOwnerMatch.DifferentOwner;
            return new ProcessOwnerInfo(match, sid);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int? ReadTokenIntegrityLevel(IntPtr tokenHandle)
    {
        ProcessInspectionNative.GetTokenInformation(
            tokenHandle,
            ProcessInspectionNative.TokenIntegrityLevel,
            IntPtr.Zero,
            0,
            out var needed);
        if (needed == 0)
            return null;

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!ProcessInspectionNative.GetTokenInformation(
                    tokenHandle,
                    ProcessInspectionNative.TokenIntegrityLevel,
                    buffer,
                    needed,
                    out _))
            {
                return null;
            }

            var sid = new SecurityIdentifier(Marshal.ReadIntPtr(buffer));
            var binary = new byte[sid.BinaryLength];
            sid.GetBinaryForm(binary, 0);
            return binary[^4]
                   | (binary[^3] << 8)
                   | (binary[^2] << 16)
                   | (binary[^1] << 24);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static ProcessOwnerInfo OwnerFromOpenFailure(int error)
    {
        return error switch
        {
            ProcessInspectionNative.ErrorAccessDenied =>
                new ProcessOwnerInfo(ProcessOwnerMatch.InaccessibleDifferentOwner, null),
            ProcessInspectionNative.ErrorInvalidParameter =>
                new ProcessOwnerInfo(ProcessOwnerMatch.Unknown, null),
            _ => new ProcessOwnerInfo(ProcessOwnerMatch.Unknown, null)
        };
    }
}
