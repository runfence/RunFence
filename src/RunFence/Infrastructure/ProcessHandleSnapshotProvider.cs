using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace RunFence.Infrastructure;

internal sealed class ProcessHandleSnapshotProvider(
    IJobObjectApi jobObjectApi,
    IProcessHandleSnapshotNative native,
    IObjectTypeNameReader objectTypeNameReader) : IProcessHandleSnapshotProvider
{
    private const string JobObjectTypeName = "Job";
    private const int InitialBufferSize = 4096;

    public IReadOnlyList<OwnedJobHandle> GetJobHandleCandidates(SafeProcessHandle ownerProcessHandle)
    {
        ArgumentNullException.ThrowIfNull(ownerProcessHandle);

        var candidates = new List<OwnedJobHandle>();
        var buffer = IntPtr.Zero;
        try
        {
            var bufferSize = InitialBufferSize;
            while (true)
            {
                buffer = Marshal.AllocHGlobal(bufferSize);
                var status = native.QueryProcessHandleInformation(
                    ownerProcessHandle,
                    buffer,
                    bufferSize,
                    out var returnLength);

                if (status == SystemHandleNative.StatusSuccess)
                    break;

                Marshal.FreeHGlobal(buffer);
                buffer = IntPtr.Zero;
                if (status is not SystemHandleNative.StatusInfoLengthMismatch
                    and not SystemHandleNative.StatusBufferOverflow
                    and not SystemHandleNative.StatusBufferTooSmall)
                {
                    throw new InvalidOperationException(
                        $"NtQueryInformationProcess(ProcessHandleInformation) failed with NTSTATUS 0x{status:X8}.");
                }

                bufferSize = Math.Max(bufferSize * 2, returnLength);
            }

            var headerSize = Marshal.SizeOf<ProcessHandleNative.PROCESS_HANDLE_SNAPSHOT_INFORMATION>();
            var entrySize = Marshal.SizeOf<ProcessHandleNative.PROCESS_HANDLE_TABLE_ENTRY_INFO>();
            var handleCount = checked((int)(nuint)Marshal.ReadIntPtr(buffer));
            for (var i = 0; i < handleCount; i++)
            {
                var entryPtr = IntPtr.Add(buffer, headerSize + i * entrySize);
                var entry = Marshal.PtrToStructure<ProcessHandleNative.PROCESS_HANDLE_TABLE_ENTRY_INFO>(entryPtr);
                if (entry.HandleValue == IntPtr.Zero)
                    continue;

                if (!TryDuplicateSameAccess(ownerProcessHandle, entry.HandleValue, out var typeProbeHandle))
                    continue;

                try
                {
                    if (!objectTypeNameReader.TryGetObjectTypeName(typeProbeHandle, out var typeName)
                        || !string.Equals(typeName, JobObjectTypeName, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }
                finally
                {
                    jobObjectApi.CloseHandle(typeProbeHandle);
                }

                if (!TryDuplicateWithReconnectAccess(ownerProcessHandle, entry.HandleValue, out var candidateHandle))
                    continue;

                AddCandidate(candidates, candidateHandle);
            }

            return candidates;
        }
        catch
        {
            foreach (var candidate in candidates)
                candidate.Dispose();

            candidates.Clear();
            throw;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
                Marshal.FreeHGlobal(buffer);
        }
    }

    private bool TryDuplicateSameAccess(
        SafeProcessHandle ownerProcessHandle,
        IntPtr sourceHandle,
        out IntPtr duplicatedHandle) =>
        native.DuplicateSameAccess(ownerProcessHandle, sourceHandle, out duplicatedHandle);

    private bool TryDuplicateWithReconnectAccess(
        SafeProcessHandle ownerProcessHandle,
        IntPtr sourceHandle,
        out IntPtr duplicatedHandle) =>
        native.DuplicateWithAccess(
            ownerProcessHandle,
            sourceHandle,
            ProcessJobManager.JobObjectReconnectAccess,
            out duplicatedHandle);

    private void AddCandidate(List<OwnedJobHandle> candidates, IntPtr candidateHandle)
    {
        OwnedJobHandle? candidate = null;
        try
        {
            candidate = new OwnedJobHandle(jobObjectApi, candidateHandle);
            foreach (var existing in candidates)
            {
                if (!jobObjectApi.AreSameJobObject(existing.Handle, candidate.Handle))
                    continue;

                return;
            }

            candidates.Add(candidate);
            candidate = null;
        }
        finally
        {
            candidate?.Dispose();
        }
    }
}
