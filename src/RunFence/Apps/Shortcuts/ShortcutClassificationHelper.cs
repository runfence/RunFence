namespace RunFence.Apps.Shortcuts;

/// <summary>
/// Pure, stateless helpers for classifying shortcut targets.
/// These methods perform no COM or IO operations.
/// </summary>
public static class ShortcutClassificationHelper
{
    /// <summary>
    /// Extracts the original arguments from a managed shortcut's argument string.
    /// Returns null if the arguments don't match the expected managed format.
    /// </summary>
    public static string? ParseManagedShortcutArgs(string currentArgs, string appId)
    {
        if (currentArgs == appId)
            return "";

        if (currentArgs.StartsWith(appId + " "))
            return currentArgs[(appId.Length + 1)..];

        return null;
    }

    /// <summary>
    /// Returns true if the shortcut targets a folder (directly or via explorer.exe with a folder argument).
    /// </summary>
    public static bool IsFolderShortcutTarget(string? target, string? args, string normalizedFolder)
    {
        if (target == null)
            return false;
        var normalizedTarget = target.TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(normalizedTarget, normalizedFolder, StringComparison.OrdinalIgnoreCase))
            return true;
        if (target.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase) &&
            args != null && args.Contains(normalizedFolder, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    /// <summary>
    /// Returns true if the shortcut name or target exe name suggests an uninstaller.
    /// </summary>
    public static bool IsUninstallShortcut(string shortcutPath, string targetPath)
    {
        var shortcutName = Path.GetFileNameWithoutExtension(shortcutPath);
        var exeName = Path.GetFileNameWithoutExtension(targetPath);
        return shortcutName.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
               exeName.StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
               exeName.Contains("uninst", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if the target exe resides in the Windows directory or is a known system executable.
    /// </summary>
    public static bool IsSystemExecutable(string targetPath)
    {
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (targetPath.StartsWith(windowsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return true;

        var fileName = Path.GetFileName(targetPath);
        return fileName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase);
    }
}
