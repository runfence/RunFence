using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.TrayIcon;

public sealed record TrayMenuBuildRequest(
    CredentialStore? CredentialStore,
    IReadOnlyList<StartMenuEntry>? DiscoveredEntries,
    Dictionary<string, Image?> IconCache,
    AppDatabase Database,
    Bitmap AppIconBitmap,
    ITrayMenuActionHandler ActionHandler);
