using Microsoft.Win32;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public sealed class FolderHandlerRunOnceMaintenance(
    ILoggingService log)
{
    private const string RunOncePath = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";

    public string BuildCommandLine(string launcherPath)
    {
        var scriptPath = Path.Combine(Path.GetDirectoryName(launcherPath)!, PathConstants.FolderHandlerUnregisterScriptName);
        if (!File.Exists(scriptPath))
        {
            log.Warn($"FolderHandlerRunOnceMaintenance: unregister script not found at {scriptPath}, skipping RunOnce");
            return string.Empty;
        }

        return $"cmd /c \"\"{scriptPath}\"\"";
    }

    public void EnsureState(FolderHandlerRegistrationChangeTracker tracker, string runOnceCommandLine)
    {
        if (string.IsNullOrEmpty(runOnceCommandLine))
        {
            tracker.DeleteValue(RunOncePath, PathConstants.FolderHandlerRunOnceValueName, isRunOnceValue: true);
            return;
        }

        tracker.SetValue(
            RunOncePath,
            PathConstants.FolderHandlerRunOnceValueName,
            runOnceCommandLine,
            RegistryValueKind.String,
            isRunOnceValue: true);
    }

    public void Remove(IRegistryKey usersRoot, string accountSid)
    {
        try
        {
            using var key = usersRoot.OpenSubKey($@"{accountSid}\{RunOncePath}", writable: true);
            if (key == null)
                return;

            key.DeleteValue(PathConstants.FolderHandlerRunOnceValueName, throwOnMissingValue: false);
            key.Flush();
        }
        catch (Exception ex)
        {
            log.Warn($"FolderHandlerRunOnceMaintenance: failed to remove RunOnce for {accountSid}: {ex.Message}");
        }
    }
}
