using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Startup;

namespace RunFence.Persistence;

public class AutoStartService(ILoggingService log, ISidResolver sidResolver) : IAutoStartService
{
    private const string ShortcutName = "RunFence.lnk";

    // Windows silently skips requireAdministrator executables in the Startup folder.
    // The .cmd wrapper is not subject to this restriction and triggers UAC for the exe instead.
    private const string CmdWrapperName = "RunFence-autostart.cmd";

    public bool IsAutoStartEnabled()
    {
        var paths = GetAllShortcutPaths();
        return paths.Any(IsValidAutoStartShortcut);
    }

    public void EnableAutoStart()
    {
        var exePath = Path.Combine(AppContext.BaseDirectory, "RunFence.exe");
        var cmdPath = GetCmdPath();
        var shortcutPath = GetShortcutPath();

        if (!File.Exists(cmdPath))
            throw new FileNotFoundException(
                $"The auto-start wrapper script was not found. Expected: {cmdPath}", cmdPath);

        ShortcutComHelper.WithShortcut(shortcutPath, sc =>
        {
            sc.TargetPath = cmdPath;
            sc.Arguments = "";
            sc.WorkingDirectory = AppContext.BaseDirectory;
            sc.Description = "RunFence auto-start";
            sc.WindowStyle = 7; // SW_SHOWMINNOACTIVE — minimize the cmd window
            sc.IconLocation = $"{exePath},0";
            sc.Save();
        });

        log.Info($"Auto-start shortcut created: {shortcutPath}");
    }

    public void DisableAutoStart()
    {
        foreach (var path in GetAllShortcutPaths())
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                log.Info($"Auto-start shortcut removed: {path}");
            }
        }
    }

    private static bool IsValidAutoStartShortcut(string shortcutPath)
    {
        if (!File.Exists(shortcutPath))
            return false;
        try
        {
            var target = ShortcutComHelper.WithShortcut(shortcutPath, sc => (string?)sc.TargetPath);
            var expectedExe = Path.Combine(AppContext.BaseDirectory, "RunFence.exe");
            // Also accept old shortcuts targeting the exe directly (backward compatibility)
            return string.Equals(target, GetCmdPath(), StringComparison.OrdinalIgnoreCase)
                   || string.Equals(target, expectedExe, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private HashSet<string> GetAllShortcutPaths() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            GetShortcutPath(),
            GetFallbackShortcutPath()
        };

    private static string GetCmdPath() =>
        Path.Combine(AppContext.BaseDirectory, CmdWrapperName);

    private string GetShortcutPath() =>
        Path.Combine(GetInteractiveUserStartupFolder(), ShortcutName);

    private static string GetFallbackShortcutPath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);

    private string GetInteractiveUserStartupFolder()
    {
        try
        {
            var sid = NativeTokenHelper.TryGetInteractiveUserSid();
            if (sid != null)
            {
                var profilePath = sidResolver.TryGetProfilePath(sid.Value);
                if (profilePath != null)
                    return Path.Combine(profilePath,
                        @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup");
            }
        }
        catch
        {
            /* fall through */
        }

        return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
    }
}