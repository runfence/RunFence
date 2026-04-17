using RunFence.Acl.Traverse;
using RunFence.Core.Models;
using RunFence.Launch.Container;

namespace RunFence.Startup;

public class EnforcementResultApplier(IAppContainerService appContainerService)
{
    public (bool TimestampsChanged, bool TraverseRetracked) ApplyToDatabase(
        EnforcementResult result, AppDatabase database)
    {
        foreach (var (appId, timestamp) in result.TimestampUpdates)
        {
            var app = database.Apps.FirstOrDefault(a => a.Id == appId);
            if (app != null) app.LastKnownExeTimestamp = timestamp;
        }

        bool traverseRetracked = false;
        foreach (var (container, traverseDir, appliedPaths) in result.TraverseGrants)
        {
            var containerSid = !string.IsNullOrEmpty(container.Sid)
                ? container.Sid
                : appContainerService.GetSid(container.Name);
            if (string.IsNullOrEmpty(containerSid)) continue;
            var traversePaths = TraversePathsHelper.GetOrCreateTraversePaths(database, containerSid);
            traverseRetracked |= TraversePathsHelper.TrackPath(traversePaths, traverseDir, appliedPaths);
        }

        return (result.TimestampUpdates.Count > 0, traverseRetracked);
    }
}
