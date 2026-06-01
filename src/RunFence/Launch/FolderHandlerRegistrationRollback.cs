using RunFence.Acl;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public class FolderHandlerRegistrationRollback(
    ILoggingService log,
    IGrantMutatorService grantMutatorService,
    ITraverseService traverseService,
    IHkuRootProvider hkuRootProvider,
    FolderHandlerRegistrationRollbackWriter rollbackWriter)
{
    public void Rollback(FolderHandlerRegistrationEffects effects)
    {
        try
        {
            if (effects.RegistrationChangeSet != null)
            {
                using var usersRoot = hkuRootProvider.OpenUsersRoot();
                rollbackWriter.RollbackRegistrationChanges(usersRoot, effects.AccountSid, effects.RegistrationChangeSet);
            }

            if (effects.AccountGrantApplied || effects.AccountTraverseApplied ||
                effects.LowIntegrityGrantApplied || effects.LowIntegrityTraverseApplied)
            {
                var launcherDir = Path.GetDirectoryName(effects.LauncherPath);
                if (!string.IsNullOrEmpty(launcherDir))
                {
                    if (effects.AccountGrantApplied)
                    {
                        var result = grantMutatorService.RemoveGrant(effects.AccountSid, launcherDir, isDeny: false);
                        LogGrantWarnings(effects.AccountSid, launcherDir, result.Warnings);
                    }
                    if (effects.AccountTraverseApplied)
                    {
                        var result = traverseService.RemoveTraverse(effects.AccountSid, launcherDir);
                        LogGrantWarnings(effects.AccountSid, launcherDir, result.Warnings);
                    }

                    if (effects.LowIntegrityGrantApplied)
                    {
                        var result = grantMutatorService.RemoveGrant(AclHelper.LowIntegritySid, launcherDir, isDeny: false);
                        LogGrantWarnings(AclHelper.LowIntegritySid, launcherDir, result.Warnings);
                    }
                    if (effects.LowIntegrityTraverseApplied)
                    {
                        var result = traverseService.RemoveTraverse(AclHelper.LowIntegritySid, launcherDir);
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
