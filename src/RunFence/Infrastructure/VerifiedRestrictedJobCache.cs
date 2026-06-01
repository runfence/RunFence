using Microsoft.Win32.SafeHandles;
using RunFence.Core;

namespace RunFence.Infrastructure;

/// <summary>
/// Foreground/bridge classification cache for previously verified restricted-job handles only.
/// Do not use it for authorization, IPC trust, launch trust, ACL/grant decisions, credential
/// decisions, or JobKeeper reconnect acceptance.
/// </summary>
public sealed class VerifiedRestrictedJobCache(
    IJobObjectApi jobObjectApi,
    VerifiedRestrictedJobAdmissionPolicy admissionPolicy,
    ILoggingService log) : IVerifiedRestrictedJobCache, IDisposable
{
    private readonly object gate = new();
    private readonly List<OwnedJobHandle> entries = [];
    private bool disposed;

    public bool TryAddDuplicate(IntPtr jobHandle)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (jobHandle == IntPtr.Zero)
            return false;

        if (!jobObjectApi.DuplicateHandleToProcess(
                ProcessNative.GetCurrentProcess(),
                jobHandle,
                ProcessNative.GetCurrentProcess(),
                ProcessJobManager.JobObjectReconnectAccess,
                out var duplicatedHandle)
            || duplicatedHandle == IntPtr.Zero)
        {
            return false;
        }

        OwnedJobHandle? ownedHandle = null;
        try
        {
            ownedHandle = new OwnedJobHandle(jobObjectApi, duplicatedHandle);
            if (!admissionPolicy.TryValidateForAdmission(
                    ownedHandle.Handle,
                    out var failureReason,
                    out var shouldLogFailure))
            {
                if (shouldLogFailure)
                    LogRejectedHandle(failureReason);

                return false;
            }

            lock (gate)
            {
                ThrowIfDisposed();
                if (!TryAddVerifiedHandle(ownedHandle))
                    return false;
            }

            ownedHandle = null;
            return true;
        }
        finally
        {
            ownedHandle?.Dispose();
        }
    }

    public VerifiedRestrictedJobMembershipResult CheckMembership(SafeProcessHandle processHandle)
    {
        ArgumentNullException.ThrowIfNull(processHandle);
        ObjectDisposedException.ThrowIf(disposed, this);

        lock (gate)
        {
            ThrowIfDisposed();
            if (entries.Count == 0)
                return VerifiedRestrictedJobMembershipResult.NoMatch;

            var sawUnknown = false;
            foreach (var entry in entries)
            {
                var isMember = jobObjectApi.IsProcessInJob(
                    processHandle.DangerousGetHandle(),
                    entry.Handle);
                if (!isMember.HasValue)
                {
                    sawUnknown = true;
                    continue;
                }

                if (isMember.Value)
                    return VerifiedRestrictedJobMembershipResult.Match;
            }

            return sawUnknown
                ? VerifiedRestrictedJobMembershipResult.Unknown
                : VerifiedRestrictedJobMembershipResult.NoMatch;
        }
    }

    public void SweepEmptyOrInvalidJobs()
    {
        ObjectDisposedException.ThrowIf(disposed, this);

        lock (gate)
        {
            ThrowIfDisposed();
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                var entry = entries[i];
                var processIds = jobObjectApi.QueryProcessIds(entry.Handle);
                if (processIds == null || processIds.Count == 0)
                {
                    RemoveAt(i);
                    continue;
                }

                if (!admissionPolicy.TryValidateCachedClassification(entry.Handle, out _))
                    RemoveAt(i);
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;
            foreach (var entry in entries)
                entry.Dispose();

            entries.Clear();
        }
    }

    private void RemoveAt(int index)
    {
        var entry = entries[index];
        entries.RemoveAt(index);
        entry.Dispose();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
    }

    private bool TryAddVerifiedHandle(OwnedJobHandle handle)
    {
        foreach (var entry in entries)
        {
            if (!jobObjectApi.AreSameJobObject(entry.Handle, handle.Handle))
                continue;

            return false;
        }

        entries.Add(handle);
        return true;
    }

    private void LogRejectedHandle(string? failureReason)
    {
        log.Info(
            $"VerifiedRestrictedJobCache: rejected duplicated keeper job handle: {failureReason ?? "unknown reason"}.");
    }
}
