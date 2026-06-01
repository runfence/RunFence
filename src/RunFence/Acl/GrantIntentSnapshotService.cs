using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Acl.Traverse;

namespace RunFence.Acl;

public class GrantIntentSnapshotService(
    GrantRuntimeSnapshotService grantRuntimeSnapshotService,
    ITraverseIntentStoreCoordinator traverseIntentStoreCoordinator,
    Func<IGrantIntentRepository> grantIntentRepository) : IGrantIntentSnapshotService
{
    private IGrantIntentRepository GrantIntentRepository => grantIntentRepository();

    public GrantIntentRestoreSnapshot CaptureGrantRestoreSnapshot(string sid, string path, bool isDeny)
    {
        var normalized = Path.GetFullPath(path);
        var locations = GrantIntentRepository.FindEntriesForSid(sid)
            .Where(location =>
                !location.Entry.IsTraverseOnly &&
                location.Entry.IsDeny == isDeny &&
                string.Equals(location.Entry.Path, normalized, StringComparison.OrdinalIgnoreCase))
            .Select(location => new GrantIntentRestoreLocation(
                new GrantIntentStoreIdentity(location.Store.ConfigPath),
                location.Entry))
            .ToList();
        var runtimeEntry = grantRuntimeSnapshotService.CaptureGrantSnapshot(sid, normalized, isDeny).Entry;
        return new GrantIntentRestoreSnapshot(runtimeEntry, locations);
    }

    public GrantIntentRestoreSnapshot CaptureTraverseRestoreSnapshot(string sid, string path)
    {
        var normalized = Path.GetFullPath(path);
        var locations = traverseIntentStoreCoordinator.GetTraverseLocationsForPath(
                sid,
                normalized,
                includeManualSharedEntries: true)
            .Select(location => new GrantIntentRestoreLocation(
                new GrantIntentStoreIdentity(location.Store.ConfigPath),
                location.Entry))
            .ToList();
        var runtimeEntry = grantRuntimeSnapshotService.CaptureTraverseSnapshot(sid, normalized).Entry;
        return new GrantIntentRestoreSnapshot(runtimeEntry, locations);
    }
}
