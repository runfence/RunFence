using RunFence.Apps.Shortcuts;
using RunFence.Core.Models;

namespace RunFence.Apps.UI;

public interface IAppDiscoveryDialogService
{
    (string path, string? name)? ShowDialog(
        IReadOnlyList<DiscoveredApp> apps,
        IShortcutIconHelper iconHelper,
        IWin32Window? owner = null);
}
