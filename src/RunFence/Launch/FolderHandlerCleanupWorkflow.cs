using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public sealed class FolderHandlerCleanupWorkflow(
    ILoggingService log,
    FolderHandlerCleanupSidSnapshotProvider cleanupSidSnapshotProvider,
    FolderHandlerSidPolicy sidPolicy,
    FolderHandlerTrackedSidState trackedSidState,
    FolderHandlerRegistrationWriter registrationWriter,
    FolderHandlerCleanupService cleanupService,
    FolderHandlerRegistrationWorkflow registrationWorkflow)
{
    public IReadOnlyList<string> CaptureCleanupSidSnapshot()
        => cleanupSidSnapshotProvider.Capture();

    public void CleanupStaleRegistrations()
        => CleanupStaleRegistrations(CaptureCleanupSidSnapshot());

    public void CleanupStaleRegistrations(IReadOnlyCollection<string> rawSessionSids)
    {
        log.Info("FolderHandlerCleanupWorkflow: cleaning up stale registrations.");
        try
        {
            trackedSidState.ExecuteLocked(() =>
            {
                var activeSids = new HashSet<string>(GetEligibleCurrentStateSids(rawSessionSids), StringComparer.OrdinalIgnoreCase);
                var staleTrackedSids = new List<string>();

                foreach (var sid in trackedSidState.Snapshot())
                {
                    if (sidPolicy.ShouldKeepRegistrationForSid(sid) && registrationWriter.HasOwnedRegistration(sid))
                        activeSids.Add(sid);
                    else
                        staleTrackedSids.Add(sid);
                }

                foreach (var sid in registrationWriter.GetOwnedRegistrationSids())
                {
                    if (sidPolicy.ShouldKeepRegistrationForSid(sid))
                        activeSids.Add(sid);
                }

                trackedSidState.Merge(activeSids, staleTrackedSids);

                var cleanedAny = cleanupService.CleanupStaleEntries(activeSids.ToList());
                if (cleanedAny)
                {
                    ShellNative.SHChangeNotify(ShellNative.SHCNE_ASSOCCHANGED, ShellNative.SHCNF_IDLIST,
                        IntPtr.Zero, IntPtr.Zero);
                }
            });
        }
        catch (Exception ex)
        {
            log.Warn($"FolderHandlerCleanupWorkflow: cleanup failed: {ex.Message}");
        }
    }

    public void UnregisterAll()
    {
        var rawSessionSids = CaptureCleanupSidSnapshot();
        var activeSids = trackedSidState.ExecuteLocked(() =>
        {
            var currentStateSids = GetTrackedOwnedAndCurrentStateSids(rawSessionSids);
            cleanupService.CleanupStaleEntries(GetEligibleCurrentStateSids(rawSessionSids));
            return currentStateSids;
        });

        foreach (var sid in activeSids)
            registrationWorkflow.Unregister(sid);
    }

    private List<string> GetTrackedOwnedAndCurrentStateSids(IReadOnlyCollection<string> rawSessionSids)
    {
        var result = new HashSet<string>(GetEligibleCurrentStateSids(rawSessionSids), StringComparer.OrdinalIgnoreCase);
        foreach (var sid in trackedSidState.Snapshot())
        {
            if (registrationWriter.HasOwnedRegistration(sid))
                result.Add(sid);
        }

        foreach (var sid in registrationWriter.GetOwnedRegistrationSids())
            result.Add(sid);

        foreach (var sid in result)
            trackedSidState.Add(sid);

        return result.ToList();
    }

    private List<string> GetEligibleCurrentStateSids(IReadOnlyCollection<string> rawSessionSids)
        => rawSessionSids
            .Where(sid => !string.IsNullOrWhiteSpace(sid))
            .Where(sidPolicy.ShouldKeepRegistrationForSid)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
