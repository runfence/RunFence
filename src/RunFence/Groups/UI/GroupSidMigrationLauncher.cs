using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.SidMigration.UI.Forms;

namespace RunFence.Groups.UI;

/// <summary>
/// Launches the SID migration workflow from the Groups panel.
/// Encapsulates dialog creation, modal display, and result handling for the migrate-SIDs action.
/// </summary>
public class GroupSidMigrationLauncher(
    IModalCoordinator modalCoordinator,
    SidMigrationDialogFactory sidMigrationDialogFactory)
{
    /// <summary>
    /// Opens the SID migration dialog. Returns <c>true</c> if an in-app migration was applied.
    /// </summary>
    public bool Launch(IWin32Window? owner)
    {
        using var dlg = sidMigrationDialogFactory.Create();
        return modalCoordinator.ShowModal(dlg, owner) == DialogResult.OK;
    }
}
