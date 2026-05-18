using RunFence.Acl;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public class FolderHandlerRegistrationRollback(
    ILoggingService log,
    IPathGrantService pathGrantService,
    FolderHandlerRegistryStore registryStore)
{
    public void Rollback(FolderHandlerRegistrationEffects effects)
    {
        try
        {
            if (effects.RegistryWritten || effects.RunOnceWritten || effects.SidTracked)
                registryStore.Unregister(effects.AccountSid);

            if (effects.AccountGrantApplied || effects.AccountTraverseApplied ||
                effects.LowIntegrityGrantApplied || effects.LowIntegrityTraverseApplied)
            {
                var launcherDir = Path.GetDirectoryName(effects.LauncherPath);
                if (!string.IsNullOrEmpty(launcherDir))
                {
                    if (effects.AccountGrantApplied)
                    {
                        var result = pathGrantService.RemoveGrant(effects.AccountSid, launcherDir, isDeny: false);
                        LogGrantWarnings(effects.AccountSid, launcherDir, result.Warnings);
                    }
                    if (effects.AccountTraverseApplied)
                    {
                        var result = pathGrantService.RemoveTraverse(effects.AccountSid, launcherDir);
                        LogGrantWarnings(effects.AccountSid, launcherDir, result.Warnings);
                    }

                    if (effects.LowIntegrityGrantApplied)
                    {
                        var result = pathGrantService.RemoveGrant(AclHelper.LowIntegritySid, launcherDir, isDeny: false);
                        LogGrantWarnings(AclHelper.LowIntegritySid, launcherDir, result.Warnings);
                    }
                    if (effects.LowIntegrityTraverseApplied)
                    {
                        var result = pathGrantService.RemoveTraverse(AclHelper.LowIntegritySid, launcherDir);
                        LogGrantWarnings(AclHelper.LowIntegritySid, launcherDir, result.Warnings);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log.Warn($"FolderHandlerRegistrationRollback: rollback failed for {effects.AccountSid}: {ex.Message}");
        }
    }

    private void LogGrantWarnings(string sid, string launcherDir, IReadOnlyList<GrantApplyWarning> warnings)
    {
        foreach (var warning in warnings)
        {
            log.Warn(
                $"FolderHandlerRegistrationRollback warning for SID '{sid}' on '{launcherDir}': " +
                GrantApplyFailureFormatter.Format(warning));
        }
    }
}
