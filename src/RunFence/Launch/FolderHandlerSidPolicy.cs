using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public sealed class FolderHandlerSidPolicy(
    ILoggingService log,
    ILocalGroupQueryService localGroupMembership)
{
    public bool ShouldKeepRegistrationForSid(string accountSid)
    {
        if (SidResolutionHelper.IsSystemSid(accountSid))
        {
            log.Info("FolderHandlerSidPolicy: skipping registration for SYSTEM account");
            return false;
        }

        if (string.Equals(accountSid, SidResolutionHelper.GetInteractiveUserSid(), StringComparison.OrdinalIgnoreCase))
        {
            log.Info($"FolderHandlerSidPolicy: skipping registration for interactive user {accountSid}");
            return false;
        }

        if (localGroupMembership.GetGroupsForUser(accountSid)
                .Any(g => string.Equals(g.Sid, "S-1-5-32-544", StringComparison.OrdinalIgnoreCase)))
        {
            log.Info($"FolderHandlerSidPolicy: skipping registration for admin account {accountSid}");
            return false;
        }

        return true;
    }
}
