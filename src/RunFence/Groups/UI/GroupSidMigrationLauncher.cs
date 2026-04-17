using RunFence.Account;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.SidMigration;
using RunFence.SidMigration.UI.Forms;

namespace RunFence.Groups.UI;

/// <summary>
/// Launches the SID migration workflow from the Groups panel.
/// Encapsulates dialog creation, modal display, and result handling for the migrate-SIDs action.
/// </summary>
public class GroupSidMigrationLauncher(
    IModalCoordinator modalCoordinator,
    ISidMigrationService sidMigrationService,
    Func<InAppMigrationHandler> createMigrationHandler,
    ILocalUserProvider localUserProvider,
    ILoggingService log,
    ISidResolver sidResolver,
    ISidNameCacheService sidNameCache)
{
    /// <summary>
    /// Opens the SID migration dialog. Returns <c>true</c> if an in-app migration was applied.
    /// </summary>
    public bool Launch(SessionContext session, IWin32Window? owner)
    {
        using var dlg = new SidMigrationDialog(session, sidMigrationService, createMigrationHandler(), localUserProvider,
            log, sidResolver, sidNameCache);
        modalCoordinator.ShowModal(dlg, owner);
        return dlg.InAppMigrationApplied;
    }
}
