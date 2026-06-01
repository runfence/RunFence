using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

/// <summary>
/// Reads and writes taskbar settings, including pinned shortcuts.
/// <para>
/// <b>Known limitation:</b> Pinned taskbar shortcuts are stored as <c>.lnk</c> files whose
/// internal target paths embed the source account's profile path. When transferring across
/// accounts, the binary blobs in <c>Favorites</c> and <c>FavoritesResolve</c> are patched to
/// replace the source profile path with the target profile path, but shortcuts that point to
/// per-user locations outside the standard profile root (e.g., per-user AppData shortcuts to
/// Store apps) may still contain stale source account paths and produce broken taskbar items on
/// the target account.
/// </para>
/// <para>
/// COM calls (<c>WScript.Shell</c> via <c>CreateShortcut</c>) require an STA thread.
/// <c>[STAThread]</c> on <c>Main</c> in <c>Program.cs</c> provides STA for the entire process.
/// This is less robust than spinning a dedicated STA thread (as in SecurityScanner), but
/// sufficient for a single-threaded CLI tool that never spawns background COM callers.
/// </para>
/// </summary>
public class TaskbarSettingsIO(
    ITaskbarRegistryStore registryStore,
    IPinnedShortcutTransferService pinnedShortcutTransferService,
    TaskbarLegacyOwnershipDetector legacyOwnershipDetector,
    TaskbarProfilePathPatcher profilePathPatcher,
    IBroadcastHelper broadcast) : ISettingsIO
{
    public TaskbarSettings Read()
    {
        var taskbar = new TaskbarSettings();
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
            taskbar.SourceProfilePath = userProfile;

        var explorerAdvanced = registryStore.ReadExplorerAdvancedValues();
        taskbar.SmallIcons = explorerAdvanced.TaskbarSmallIcons;
        taskbar.ShowTaskViewButton = explorerAdvanced.ShowTaskViewButton;
        taskbar.TaskbarAlignment = explorerAdvanced.TaskbarAlignment;
        taskbar.ShowWidgets = explorerAdvanced.ShowWidgets;
        taskbar.ButtonCombine = explorerAdvanced.ButtonCombine;
        taskbar.MultiMonitorButtonCombine = explorerAdvanced.MultiMonitorButtonCombine;
        taskbar.VirtualDesktopTaskbarFilter = explorerAdvanced.VirtualDesktopTaskbarFilter;

        var taskband = registryStore.ReadTaskbandValues();
        taskbar.Favorites = taskband.Favorites;
        taskbar.FavoritesResolve = taskband.FavoritesResolve;
        taskbar.SearchboxTaskbarMode = registryStore.ReadSearchboxTaskbarMode();
        pinnedShortcutTransferService.ReadPinnedShortcuts(taskbar);
        return taskbar;
    }

    public void Write(TaskbarSettings taskbar)
    {
        bool changed = false;
        changed |= registryStore.WriteExplorerAdvancedValues(new TaskbarExplorerAdvancedRegistryValues
        {
            TaskbarSmallIcons = taskbar.SmallIcons,
            ShowTaskViewButton = taskbar.ShowTaskViewButton,
            TaskbarAlignment = taskbar.TaskbarAlignment,
            ShowWidgets = taskbar.ShowWidgets,
            ButtonCombine = taskbar.ButtonCombine,
            MultiMonitorButtonCombine = taskbar.MultiMonitorButtonCombine,
            VirtualDesktopTaskbarFilter = taskbar.VirtualDesktopTaskbarFilter
        });
        changed |= registryStore.WriteTaskbandValues(CreateWritableTaskbandValues(taskbar));
        changed |= pinnedShortcutTransferService.WritePinnedShortcuts(taskbar);
        if (taskbar.SearchboxTaskbarMode.HasValue)
            changed |= registryStore.WriteSearchboxTaskbarMode(taskbar.SearchboxTaskbarMode.Value);
        if (changed)
            broadcast.Broadcast();
    }

    private TaskbarTaskbandRegistryValues CreateWritableTaskbandValues(TaskbarSettings taskbar)
    {
        if (taskbar.Favorites == null && taskbar.FavoritesResolve == null)
            return new TaskbarTaskbandRegistryValues();

        var targetProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(targetProfile))
            return new TaskbarTaskbandRegistryValues();

        byte[]? favorites = taskbar.Favorites;
        byte[]? favoritesResolve = taskbar.FavoritesResolve;

        if (!string.IsNullOrEmpty(taskbar.SourceProfilePath) &&
            !string.Equals(taskbar.SourceProfilePath, targetProfile, StringComparison.OrdinalIgnoreCase))
        {
            favorites = profilePathPatcher.PatchProfilePath(favorites, taskbar.SourceProfilePath, targetProfile);
            favoritesResolve = profilePathPatcher.PatchProfilePath(favoritesResolve, taskbar.SourceProfilePath, targetProfile);
        }
        else if (string.IsNullOrEmpty(taskbar.SourceProfilePath) &&
                 !legacyOwnershipDetector.IsOwnedByCurrentProfile(taskbar.Favorites, taskbar.FavoritesResolve, targetProfile))
        {
            return new TaskbarTaskbandRegistryValues();
        }

        return new TaskbarTaskbandRegistryValues
        {
            Favorites = favorites,
            FavoritesResolve = favoritesResolve
        };
    }

    void ISettingsIO.ReadInto(UserSettings s) => s.Taskbar = Read();
    void ISettingsIO.WriteFrom(UserSettings s) { if (s.Taskbar != null) Write(s.Taskbar); }
}
