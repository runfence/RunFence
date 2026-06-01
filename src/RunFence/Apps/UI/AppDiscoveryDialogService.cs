using RunFence.Apps.Shortcuts;
using RunFence.Apps.UI.Forms;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Apps.UI;

public sealed class AppDiscoveryDialogService : IAppDiscoveryDialogService
{
    private readonly IModalCoordinator _modalCoordinator;

    public AppDiscoveryDialogService(IModalCoordinator modalCoordinator)
    {
        _modalCoordinator = modalCoordinator;
    }

    public (string path, string? name)? ShowDialog(
        IReadOnlyList<DiscoveredApp> apps,
        IShortcutIconHelper iconHelper,
        IWin32Window? owner = null)
    {
        ArgumentNullException.ThrowIfNull(apps);
        ArgumentNullException.ThrowIfNull(iconHelper);

        using var dialog = new AppDiscoveryDialog(apps.ToList(), iconHelper);
        var dialogResult = _modalCoordinator.ShowModal(dialog, owner);
        return dialogResult == DialogResult.OK
            ? (dialog.SelectedPath!, dialog.SelectedName)
            : null;
    }
}
