using PrefTrans.Settings;

namespace PrefTrans.Services.IO;

public class PinnedShortcutTransferService(
    ISafeExecutor safe,
    IUserProfileFilter userProfileFilter,
    TaskbarProfilePathPatcher profilePathPatcher,
    IPinnedShortcutFolderProvider pinnedShortcutFolderProvider,
    IPinnedShortcutReader pinnedShortcutReader,
    IPinnedShortcutFileStore pinnedShortcutFileStore)
    : IPinnedShortcutTransferService
{
    public void ReadPinnedShortcuts(TaskbarSettings taskbar)
    {
        safe.Try(() =>
        {
            var pinnedFolder = pinnedShortcutFolderProvider.GetPinnedShortcutFolder();
            var profilePaths = userProfileFilter.GetUserProfilePaths();
            var lnkFiles = pinnedShortcutFileStore.EnumerateShortcutFiles(pinnedFolder);
            if (lnkFiles.Count == 0)
                return;

            var shortcuts = new List<string>();
            var shortcutFiles = new Dictionary<string, byte[]>();
            foreach (var lnkPath in lnkFiles)
            {
                safe.Try(() =>
                {
                    var target = pinnedShortcutReader.ReadTargetPath(lnkPath);
                    if (string.IsNullOrEmpty(target))
                        return;
                    if (userProfileFilter.ContainsUserProfilePath(target, profilePaths))
                        return;
                    if (userProfileFilter.ContainsWindowsAppsPath(target))
                        return;

                    var fileName = Path.GetFileName(lnkPath);
                    shortcuts.Add(fileName);
                    safe.Try(() => shortcutFiles[fileName] = pinnedShortcutFileStore.ReadAllBytes(lnkPath), "reading");
                }, "reading");
            }

            if (shortcuts.Count > 0)
                taskbar.PinnedShortcuts = shortcuts;
            if (shortcutFiles.Count > 0)
                taskbar.PinnedShortcutFiles = shortcutFiles;
        }, "reading");
    }

    public bool WritePinnedShortcuts(TaskbarSettings taskbar)
    {
        bool wroteShortcut = false;
        safe.Try(() =>
        {
            var taskBarFolder = pinnedShortcutFolderProvider.GetPinnedShortcutFolder();

            if (taskbar.PinnedShortcutFiles != null)
            {
                pinnedShortcutFileStore.EnsureDirectory(taskBarFolder);
                var targetProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var sourceProfile = taskbar.SourceProfilePath;
                bool shouldPatch = !string.IsNullOrEmpty(sourceProfile) &&
                                   !string.IsNullOrEmpty(targetProfile) &&
                                   !string.Equals(sourceProfile, targetProfile, StringComparison.OrdinalIgnoreCase);
                foreach (var (fileName, content) in taskbar.PinnedShortcutFiles)
                {
                    safe.Try(() =>
                    {
                        if (!profilePathPatcher.TryResolvePinnedShortcutDestinationPath(taskBarFolder, fileName, out var destinationPath))
                        {
                            Console.Error.WriteLine($"Warning: skipped invalid pinned shortcut name: {fileName}");
                            return;
                        }

                        var patched = shouldPatch
                            ? profilePathPatcher.PatchProfilePath(content, sourceProfile!, targetProfile) ?? content
                            : content;
                        pinnedShortcutFileStore.WriteAllBytes(destinationPath, patched);
                        wroteShortcut = true;
                    }, "writing");
                }

                return;
            }

            if (taskbar.PinnedShortcuts == null)
                return;

            var existingShortcutPaths = pinnedShortcutFileStore.EnumerateShortcutFiles(taskBarFolder);
            foreach (var name in taskbar.PinnedShortcuts)
            {
                if (!profilePathPatcher.TryResolvePinnedShortcutDestinationPath(taskBarFolder, name, out var destinationPath))
                {
                    Console.Error.WriteLine($"Warning: skipped invalid pinned shortcut name: {name}");
                    continue;
                }

                if (!existingShortcutPaths.Contains(destinationPath, StringComparer.OrdinalIgnoreCase))
                    Console.Error.WriteLine($"Warning: pinned shortcut not found: {name}");
            }
        }, "writing");
        return wroteShortcut;
    }
}
