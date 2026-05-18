using RunFence.Core;

namespace RunFence.Apps.Shortcuts;

/// <summary>
/// Scans and finds shortcuts in a <see cref="ShortcutTraversalCache"/> by target path, folder,
/// or launcher app ID. Separated from shortcut create/delete/update operations in
/// <see cref="ShortcutService"/>.
/// </summary>
public class ShortcutFinder
{
    /// <summary>
    /// Scanned shortcut managed by RunFence's launcher. Carries both the shortcut path and the
    /// target path recorded at scan time so updates can detect launcher-path drift.
    /// </summary>
    public readonly record struct ManagedShortcut(string Path, string TargetPath);

    /// <summary>
    /// Returns all shortcut paths whose target resolves to the same path as <paramref name="exePath"/>.
    /// </summary>
    public List<string> FindShortcutsForExe(string exePath, ShortcutTraversalCache cache)
    {
        // Managed shortcut matching here is path-based discovery only; it is not used as ownership proof.
        var normalized = Path.GetFullPath(exePath);
        return cache.FindWhere((target, _) =>
        {
            if (target == null)
                return false;
            try
            {
                return string.Equals(Path.GetFullPath(target), normalized, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        });
    }

    /// <summary>
    /// Returns all shortcut paths that target the given folder (via folder-shortcut conventions).
    /// </summary>
    public List<string> FindShortcutsForFolder(string folderPath, ShortcutTraversalCache cache)
    {
        // Folder-target matching is limited to managed shortcut discovery heuristics, not ownership verification.
        var normalizedFolder = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return cache.FindWhere((target, args) =>
            ShortcutClassificationHelper.IsFolderShortcutTarget(target, args, normalizedFolder));
    }

    /// <summary>
    /// Returns all shortcut paths that point to the RunFence launcher with <paramref name="appId"/> as the first argument.
    /// </summary>
    public List<string> FindShortcutsForLauncher(string appId, ShortcutTraversalCache cache)
    {
        // Suffix/target matching is intentional for RunFence-managed launcher shortcuts only;
        // it is not intended as a general ownership proof for arbitrary shortcut files.
        return cache.FindWhere((target, args) =>
            target != null &&
            target.EndsWith(PathConstants.LauncherExeName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(ShortcutClassificationHelper.TryGetManagedShortcutAppId(args), appId, StringComparison.Ordinal));
    }

    /// <summary>
    /// Scans all shortcuts in the cache and returns two lookup dictionaries:
    /// one by normalized target path, one by RunFence launcher app ID.
    /// </summary>
    public (Dictionary<string, List<string>> ByTarget, Dictionary<string, List<ManagedShortcut>> ByLauncherAppId)
        ScanAllShortcuts(ShortcutTraversalCache cache)
    {
        var byTarget = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var byLauncherAppId = new Dictionary<string, List<ManagedShortcut>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (lnk, target, args) in cache.Entries)
        {
            if (target == null)
                continue;

            string normalizedTarget;
            try { normalizedTarget = Path.GetFullPath(target); }
            catch { normalizedTarget = target; }

            if (!byTarget.TryGetValue(normalizedTarget, out var targetList))
            {
                targetList = [];
                byTarget[normalizedTarget] = targetList;
            }

            targetList.Add(lnk);

            if (target.EndsWith(PathConstants.LauncherExeName, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(args))
            {
                var appId = ShortcutClassificationHelper.TryGetManagedShortcutAppId(args);
                if (string.IsNullOrEmpty(appId))
                    continue;

                if (!byLauncherAppId.TryGetValue(appId, out var idList))
                {
                    idList = [];
                    byLauncherAppId[appId] = idList;
                }

                idList.Add(new ManagedShortcut(lnk, target));
            }
        }

        return (byTarget, byLauncherAppId);
    }
}
