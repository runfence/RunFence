using System.Runtime.InteropServices;

namespace RunFence.Apps.Shortcuts;

/// <summary>
/// Low-level COM helpers for reading and writing .lnk shortcut files via WScript.Shell.
/// Pure static utilities with no logging or state — usable from any layer.
/// </summary>
public static class ShortcutComHelper
{
    public static T WithShortcut<T>(string shortcutPath, Func<dynamic, T> action)
    {
        dynamic? shell = null;
        dynamic? shortcut = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")!;
            shell = Activator.CreateInstance(shellType)!;
            shortcut = shell.CreateShortcut(shortcutPath);
            return action(shortcut);
        }
        finally
        {
            if (shortcut != null)
                Marshal.ReleaseComObject(shortcut);
            if (shell != null)
                Marshal.ReleaseComObject(shell);
        }
    }

    public static void WithShortcut(string shortcutPath, Action<dynamic> action)
    {
        WithShortcut<object?>(shortcutPath, sc =>
        {
            action(sc);
            return null;
        });
    }

    public static (string? target, string? args) GetShortcutTargetAndArgs(string shortcutPath)
    {
        return WithShortcut(shortcutPath, sc => ((string?)sc.TargetPath, (string?)sc.Arguments));
    }

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

    public static bool IsFolderShortcutTarget(string? target, string? args, string normalizedFolder)
    {
        if (target == null)
            return false;
        // Match shortcuts targeting the folder directly (with or without trailing separator)
        var normalizedTarget = target.TrimEnd(Path.DirectorySeparatorChar);
        if (string.Equals(normalizedTarget, normalizedFolder, StringComparison.OrdinalIgnoreCase))
            return true;
        // Match explorer.exe shortcuts whose args reference the folder
        if (target.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase) &&
            args != null && args.Contains(normalizedFolder, StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    public static bool IsUninstallShortcut(string shortcutPath, string targetPath)
    {
        var shortcutName = Path.GetFileNameWithoutExtension(shortcutPath);
        var exeName = Path.GetFileNameWithoutExtension(targetPath);
        return shortcutName.Contains("uninstall", StringComparison.OrdinalIgnoreCase) ||
               exeName.StartsWith("unins", StringComparison.OrdinalIgnoreCase) ||
               exeName.Contains("uninst", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSystemExecutable(string targetPath)
    {
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (targetPath.StartsWith(windowsDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return true;

        var fileName = Path.GetFileName(targetPath);
        return fileName.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase);
    }
}