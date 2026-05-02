using RunFence.Apps.Shortcuts;
using RunFence.Core;
using RunFence.Startup;

namespace RunFence.Persistence;

public class AutoStartService(
    ILoggingService log,
    IShortcutComHelper shortcutHelper,
    IShortcutOperationRunner operationRunner,
    IAutoStartShortcutStore shortcutStore) : IAutoStartService
{
    // Windows silently skips requireAdministrator executables in the Startup folder.
    // The .cmd wrapper is not subject to this restriction and triggers UAC for the exe instead.

    public Task<bool> IsAutoStartEnabled() =>
        Task.Run(() => shortcutStore.ShortcutPaths.Any(IsValidAutoStartShortcut));

    public async Task EnableAutoStart()
    {
        await Task.Run(() =>
        {
            var shortcutPath = CreateShortcut();
            log.Info($"Auto-start shortcut created: {shortcutPath}");
        });
    }

    public Task DisableAutoStart() => Task.Run(DeleteShortcuts);

    private string CreateShortcut()
    {
        var exePath = shortcutStore.RunFenceExePath;
        var cmdPath = shortcutStore.CmdWrapperPath;
        var shortcutPath = shortcutStore.PrimaryShortcutPath;

        if (!shortcutStore.FileExists(cmdPath))
        {
            throw new FileNotFoundException(
                $"The auto-start wrapper script was not found. Expected: {cmdPath}", cmdPath);
        }

        operationRunner.Run(
            () => shortcutHelper.WithShortcut(shortcutPath, sc =>
            {
                sc.TargetPath = cmdPath;
                sc.Arguments = "";
                sc.WorkingDirectory = AppContext.BaseDirectory;
                sc.Description = "RunFence auto-start";
                sc.WindowStyle = 7; // SW_SHOWMINNOACTIVE - minimize the cmd window
                sc.IconLocation = $"{exePath},0";
                sc.Save();
            }),
            "CreateShortcut");

        return shortcutPath;
    }

    private void DeleteShortcuts()
    {
        foreach (var path in shortcutStore.ShortcutPaths)
        {
            if (!shortcutStore.FileExists(path))
                continue;

            shortcutStore.DeleteFile(path);
            log.Info($"Auto-start shortcut removed: {path}");
        }
    }

    private bool IsValidAutoStartShortcut(string shortcutPath)
    {
        if (!shortcutStore.FileExists(shortcutPath))
            return false;
        try
        {
            var target = operationRunner.Run(
                () => shortcutHelper.WithShortcut(shortcutPath, sc => (string?)sc.TargetPath),
                "ReadShortcutTarget",
                timeoutValue: null);

            var expectedExe = shortcutStore.RunFenceExePath;
            // Also accept old shortcuts targeting the exe directly (backward compatibility)
            return string.Equals(target, shortcutStore.CmdWrapperPath, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(target, expectedExe, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
