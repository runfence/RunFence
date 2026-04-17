using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.RunAs;

/// <summary>
/// Handles shortcut (.lnk) resolution for the RunAs flow, including target path extraction
/// and managed-app lookup.
/// </summary>
public class RunAsShortcutHelper(IShortcutComHelper shortcutHelper)
{
    public record struct ResolvedShortcut(string ResolvedPath, string? ShortcutArgs, ShortcutContext Context);

    /// <summary>
    /// Tries to resolve a .lnk shortcut to its target path, arguments, and shortcut context.
    /// Returns null if the shortcut is broken or references a removed managed app entry.
    /// </summary>
    public ResolvedShortcut? TryResolveShortcut(string lnkPath, IReadOnlyList<AppEntry> apps)
    {
        var (target, args) = shortcutHelper.GetShortcutTargetAndArgs(lnkPath);
        if (string.IsNullOrEmpty(target))
            return null;

        if (target.EndsWith(Constants.LauncherExeName, StringComparison.OrdinalIgnoreCase))
        {
            string? appId = null;
            if (!string.IsNullOrEmpty(args))
            {
                var spaceIndex = args.IndexOf(' ');
                appId = spaceIndex < 0 ? args : args[..spaceIndex];
            }

            if (string.IsNullOrEmpty(appId))
                return null;

            var app = apps.FirstOrDefault(a =>
                string.Equals(a.Id, appId, StringComparison.OrdinalIgnoreCase));

            if (app == null)
                return null;

            return new ResolvedShortcut(app.ExePath, null, new ShortcutContext(lnkPath, true, app));
        }

        return new ResolvedShortcut(target, string.IsNullOrEmpty(args) ? null : args, new ShortcutContext(lnkPath, false, null));
    }

    /// <summary>
    /// Attempts to resolve a .lnk file path to its target, updating <paramref name="filePath"/>,
    /// <paramref name="arguments"/>, and <paramref name="shortcutContext"/> in-place.
    /// Returns false and shows an error dialog if resolution fails; returns true otherwise.
    /// </summary>
    public bool TryHandleLnkPath(
        ref string filePath,
        ref string? arguments,
        out string? originalLnkPath,
        out ShortcutContext? shortcutContext,
        IReadOnlyList<AppEntry> apps)
    {
        originalLnkPath = null;
        shortcutContext = null;

        if (!filePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || !File.Exists(filePath))
            return true;

        var resolved = TryResolveShortcut(filePath, apps);
        if (resolved == null)
        {
            MessageBox.Show(
                "Could not resolve shortcut target.\n\nThe shortcut may be broken or reference a removed app entry.",
                "RunFence", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        originalLnkPath = filePath;
        shortcutContext = resolved.Value.Context;
        filePath = resolved.Value.ResolvedPath;
        if (!string.IsNullOrEmpty(resolved.Value.ShortcutArgs) && string.IsNullOrEmpty(arguments))
            arguments = resolved.Value.ShortcutArgs;

        return true;
    }
}
