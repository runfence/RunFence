using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Launch;

/// <summary>
/// Resolves .lnk shortcut files to their target path, arguments, and shortcut context.
/// Used by <see cref="LaunchTargetResolver"/> to handle shortcuts uniformly across all launch paths.
/// </summary>
public class ShortcutTargetResolver(IShortcutGateway shortcutGateway)
{
    public record struct ResolvedShortcut(string ResolvedPath, string? ShortcutArgs, string? ShortcutWorkingDirectory, ShortcutContext Context);

    /// <summary>
    /// Tries to resolve a .lnk shortcut to its target path, arguments, working directory, and shortcut context.
    /// Returns null if the shortcut is broken or references a removed managed app entry.
    /// </summary>
    public ResolvedShortcut? TryResolveShortcut(string lnkPath, IReadOnlyList<AppEntry> apps)
    {
        var info = shortcutGateway.Read(lnkPath);
        if (string.IsNullOrEmpty(info.TargetPath))
            return null;

        if (string.Equals(Path.GetFileName(info.TargetPath), PathConstants.LauncherExeName, StringComparison.OrdinalIgnoreCase))
        {
            string? appId = null;
            if (!string.IsNullOrEmpty(info.Arguments))
            {
                var spaceIndex = info.Arguments.IndexOf(' ');
                appId = spaceIndex < 0 ? info.Arguments : info.Arguments[..spaceIndex];
            }

            if (string.IsNullOrEmpty(appId))
                return null;

            var app = apps.FirstOrDefault(a =>
                string.Equals(a.Id, appId, StringComparison.OrdinalIgnoreCase));

            if (app == null)
                return null;

            return new ResolvedShortcut(app.ExePath, null, null, new ShortcutContext(lnkPath, true, app));
        }

            return new ResolvedShortcut(
            info.TargetPath,
            string.IsNullOrEmpty(info.Arguments) ? null : info.Arguments,
            string.IsNullOrEmpty(info.WorkingDirectory) ? null : info.WorkingDirectory,
            new ShortcutContext(lnkPath, false, null));
    }
}
