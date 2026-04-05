using RunFence.Core.Models;

namespace RunFence.Account.UI;

/// <summary>
/// Launches SID migration and orphaned-profiles dialogs for the accounts panel.
/// Encapsulates the SID lifecycle operations that share the <see cref="AccountMigrationOrchestrator"/> dependency.
/// </summary>
public class AccountSidMigrationLauncher(AccountMigrationOrchestrator migrationOrchestrator)
{
    /// <summary>
    /// Opens the SID migration dialog and returns <c>true</c> if a migration was applied.
    /// </summary>
    public bool LaunchMigrationDialog(SessionContext session, IWin32Window? owner, Action onMigrationApplied)
    {
        bool applied = false;
        migrationOrchestrator.MigrateSids(session, owner as Form, () =>
        {
            applied = true;
            onMigrationApplied();
        });
        return applied;
    }

    /// <summary>
    /// Opens the orphaned profiles dialog.
    /// </summary>
    public void LaunchOrphanedProfilesDialog(IWin32Window? owner)
        => migrationOrchestrator.DeleteProfiles(owner as Form);
}