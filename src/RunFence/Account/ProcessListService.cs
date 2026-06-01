using RunFence.Acl;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Account;

public class ProcessListService(
    ILoggingService log,
    IProcessSnapshotSource processSnapshotSource,
    IProcessJobManager processJobManager,
    Func<ITrackingJobStateStore> trackingJobStateStoreFactory) : IProcessListService
{
    public IReadOnlyList<ProcessInfo> GetProcessesForSid(string sid, CancellationToken cancellationToken = default)
    {
        var result = new List<ProcessInfo>();
        int tokenInfoClass = ProcessNative.GetTokenInfoClass(sid);
        var trackingJobStateStore = trackingJobStateStoreFactory();
        HashSet<int>? allowedPids = SidResolutionHelper.NeedsProcessJobTracking(sid)
            ? (processJobManager.GetJobMembers(sid, trackingJobStateStore.ContainsTrackingJobSid(sid)) ?? [])
            : null;

        foreach (var pid in processSnapshotSource.GetProcessIds())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pid <= 4)
                continue;

            try
            {
                CollectProcess(pid, sid, tokenInfoClass, allowedPids, result);
            }
            catch (Exception ex)
            {
                log.Warn($"ProcessListService: failed to collect process {pid}: {ex.Message}");
            }
        }

        return result;
    }

    public HashSet<string> GetSidsWithProcesses(IEnumerable<string> sids, CancellationToken cancellationToken = default)
    {
        var sidSet = sids.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (sidSet.Count == 0)
            return found;

        var containerSids = sidSet
            .Where(AclHelper.IsContainerSid)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var regularSids = sidSet
            .Where(s => !AclHelper.IsContainerSid(s) && !AclHelper.IsLowIntegritySid(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var parentFilteredSids = regularSids
            .Where(SidResolutionHelper.NeedsProcessJobTracking)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var trackingJobStateStore = parentFilteredSids.Count > 0 ? trackingJobStateStoreFactory() : null;

        Dictionary<string, HashSet<int>>? jobMembersBySid = null;
        if (parentFilteredSids.Count > 0)
        {
            jobMembersBySid = new Dictionary<string, HashSet<int>>(StringComparer.OrdinalIgnoreCase);
            foreach (var sid in parentFilteredSids)
                jobMembersBySid[sid] = processJobManager.GetJobMembers(sid, trackingJobStateStore!.ContainsTrackingJobSid(sid)) ?? [];
        }

        foreach (var pid in processSnapshotSource.GetProcessIds())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (pid <= 4)
                continue;
            if (found.Count == sidSet.Count)
                break;

            try
            {
                if (processSnapshotSource.HasExited(pid))
                    continue;

                if (regularSids.Count > 0)
                {
                    string? processSid = processSnapshotSource.GetTokenSid(pid, ProcessNative.TokenUser);
                    if (processSid != null && regularSids.Contains(processSid))
                    {
                        bool allowed = !parentFilteredSids.Contains(processSid) ||
                            (jobMembersBySid != null &&
                             jobMembersBySid.TryGetValue(processSid, out var members) &&
                             members.Contains(pid));
                        if (allowed)
                            found.Add(processSid);
                    }
                }

                if (containerSids.Count > 0)
                {
                    string? containerSid = processSnapshotSource.GetAppContainerSid(pid);
                    if (containerSid != null && containerSids.Contains(containerSid))
                        found.Add(containerSid);
                }
            }
            catch (Exception ex)
            {
                log.Warn($"ProcessListService: failed to query process {pid}: {ex.Message}");
            }
        }

        return found;
    }

    private void CollectProcess(int pid, string sid, int tokenInfoClass, HashSet<int>? allowedPids, List<ProcessInfo> result)
    {
        if (allowedPids != null && !allowedPids.Contains(pid))
            return;

        if (!string.Equals(processSnapshotSource.GetTokenSid(pid, tokenInfoClass), sid, StringComparison.OrdinalIgnoreCase))
            return;

        if (processSnapshotSource.HasExited(pid))
            return;

        var processInfo = processSnapshotSource.ReadProcessInfo(pid);
        if (processInfo == null)
            return;

        if (processSnapshotSource.HasExited(pid))
            return;

        result.Add(processInfo);
    }
}
