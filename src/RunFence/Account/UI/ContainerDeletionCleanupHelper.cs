using RunFence.Acl;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Account.UI;

public class ContainerDeletionCleanupHelper(
    AppEntryEnforcementHelper enforcementHelper,
    IAclService aclService,
    IIconService iconService,
    ILoggingService log)
{
    public void CleanupContainerApps(List<AppEntry> containerApps, List<AppEntry> remainingApps)
    {
        foreach (var app in containerApps)
        {
            try
            {
                enforcementHelper.RevertChanges(app, remainingApps);
                iconService.DeleteIcon(app.Id);
            }
            catch (Exception ex)
            {
                log.Error($"Failed to revert changes for app '{app.Name}' during container delete", ex);
            }
        }

        if (containerApps.Count > 0)
        {
            try
            {
                aclService.RecomputeAllAncestorAcls(remainingApps);
            }
            catch (Exception ex)
            {
                log.Error("Failed to recompute ancestor ACLs after container delete", ex);
            }
        }
    }
}