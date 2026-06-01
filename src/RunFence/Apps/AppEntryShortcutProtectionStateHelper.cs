using RunFence.Core.Models;

namespace RunFence.Apps;

public static class AppEntryShortcutProtectionStateHelper
{
    public static void ApplyExistingEditState(AppEntry previousApp, AppEntry newApp, AppEntryChangeSet changeSet)
    {
        ArgumentNullException.ThrowIfNull(previousApp);
        ArgumentNullException.ThrowIfNull(newApp);

        if (changeSet.RequiresManagedShortcutRefresh || changeSet.RequiresBesideTargetRefresh)
        {
            newApp.ShortcutProtectionStates = null;
            return;
        }

        newApp.ShortcutProtectionStates = previousApp.ShortcutProtectionStates?.ToList();
    }
}
