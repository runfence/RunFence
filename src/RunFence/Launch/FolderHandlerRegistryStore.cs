using Microsoft.Win32;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public class FolderHandlerRegistryStore(
    ILoggingService log,
    RegistryKey? hkuOverride = null,
    string? launcherPathOverride = null,
    string? shellServerPathOverride = null)
{
    private readonly RegistryKey _hku = hkuOverride ?? Registry.Users;
    private readonly HashSet<string> _registeredSids = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string accountSid, string commandValue)
    {
        SetCommandValue(accountSid, @"Directory\shell\open\command", commandValue);
        SetCommandValue(accountSid, @"Directory\shell\explore\command", commandValue);
        SetDirectoryShellDefaultVerb(accountSid, "open");
        SetFolderCommandValue(accountSid, commandValue);
        ReconcileTrackedState(accountSid);
    }

    public void Unregister(string accountSid)
    {
        CreateOwnedRegistryCleaner(accountSid).UnregisterOwnedFolderHandler();
        RemoveRunOnce(accountSid);
        _registeredSids.Remove(accountSid);
        ReconcileTrackedState(accountSid);
    }

    public bool IsRegistered(string accountSid) => _registeredSids.Contains(accountSid);

    public IReadOnlyList<string> GetRegisteredSids() => _registeredSids.ToList();

    public bool CleanupStaleEntries()
    {
        var launcherExeName = GetLauncherExeName();
        var shellServerExeName = GetShellServerExeName();

        var cleanedAny = false;
        foreach (var sidName in _hku.GetSubKeyNames())
            cleanedAny |= CleanupStaleForSid(sidName, launcherExeName, shellServerExeName);
        return cleanedAny;
    }

    public bool WriteRunOnce(string accountSid, string launcherPath)
    {
        var scriptPath = Path.Combine(Path.GetDirectoryName(launcherPath)!, PathConstants.FolderHandlerUnregisterScriptName);
        if (!File.Exists(scriptPath))
        {
            log.Warn($"FolderHandlerRegistryStore: unregister script not found at {scriptPath}, skipping RunOnce");
            return false;
        }

        var fullPath = $@"{accountSid}\Software\Microsoft\Windows\CurrentVersion\RunOnce";
        using var key = _hku.CreateSubKey(fullPath)
                        ?? throw new InvalidOperationException($"Failed to create registry key: {fullPath}");
        key.SetValue(PathConstants.FolderHandlerRunOnceValueName, $"cmd /c \"\"{scriptPath}\"\"");
        return true;
    }

    private bool CleanupStaleForSid(string sidName, string launcherExeName, string shellServerExeName)
    {
        var cleaned = CreateOwnedRegistryCleaner(
                sidName,
                launcherExeName,
                shellServerExeName,
                (relativePath, ex) =>
                    log.Warn($"FolderHandlerRegistryStore: failed to delete stale folder-handler key {sidName}\\Software\\Classes\\{relativePath}: {ex.Message}"))
            .UnregisterOwnedFolderHandler();

        if (cleaned)
            log.Info($"FolderHandlerRegistryStore: removed stale registration for {sidName}");
        return cleaned;
    }

    private void SetCommandValue(string accountSid, string subKeyPath, string commandValue)
    {
        var fullPath = $@"{accountSid}\Software\Classes\{subKeyPath}";
        using var key = _hku.CreateSubKey(fullPath)
                        ?? throw new InvalidOperationException($"Failed to create registry key: {fullPath}");
        key.SetValue(null, commandValue);
    }

    private void SetDirectoryShellDefaultVerb(string accountSid, string verb)
    {
        var fullPath = $@"{accountSid}\Software\Classes\Directory\shell";
        using var key = _hku.CreateSubKey(fullPath)
                        ?? throw new InvalidOperationException($"Failed to create registry key: {fullPath}");
        if (key.GetValue(PathConstants.RunFenceFallbackValueName) == null)
            key.SetValue(PathConstants.RunFenceFallbackValueName, key.GetValue(null) as string ?? string.Empty);
        key.SetValue(null, verb);
    }

    private void SetFolderCommandValue(string accountSid, string commandValue)
    {
        var fullPath = $@"{accountSid}\Software\Classes\Folder\shell\open\command";
        using var key = _hku.CreateSubKey(fullPath)
                        ?? throw new InvalidOperationException($"Failed to create registry key: {fullPath}");
        key.SetValue(null, commandValue);
        key.SetValue("DelegateExecute", "");
    }

    private void RemoveRunOnce(string accountSid)
    {
        try
        {
            using var key = _hku.OpenSubKey(
                $@"{accountSid}\Software\Microsoft\Windows\CurrentVersion\RunOnce", writable: true);
            key?.DeleteValue(PathConstants.FolderHandlerRunOnceValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            log.Warn($"FolderHandlerRegistryStore: failed to remove RunOnce for {accountSid}: {ex.Message}");
        }
    }

    private void ReconcileTrackedState(string accountSid)
    {
        var cleaner = CreateOwnedRegistryCleaner(accountSid);
        var hasDirectoryOpen = cleaner.HasOwnedCommandValue(FolderHandlerOwnedRegistryCleaner.DirectoryOpenCommandPath);
        var hasDirectoryExplore = cleaner.HasOwnedCommandValue(FolderHandlerOwnedRegistryCleaner.DirectoryExploreCommandPath);
        var hasFolderOpen = cleaner.HasOwnedCommandValue(FolderHandlerOwnedRegistryCleaner.FolderOpenCommandPath);

        if (hasDirectoryOpen && hasDirectoryExplore && hasFolderOpen)
            _registeredSids.Add(accountSid);
        else
            _registeredSids.Remove(accountSid);
    }

    private string GetLauncherExeName()
    {
        return Path.GetFileName(
            launcherPathOverride ?? Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName));
    }

    private string GetShellServerExeName()
    {
        return Path.GetFileName(
            shellServerPathOverride ?? Path.Combine(AppContext.BaseDirectory, PathConstants.ShellServerExeName));
    }

    private FolderHandlerOwnedRegistryCleaner CreateOwnedRegistryCleaner(
        string sidName,
        string? launcherExeName = null,
        string? shellServerExeName = null,
        Action<string, Exception>? onError = null)
    {
        return new FolderHandlerOwnedRegistryCleaner(
            _hku,
            $@"{sidName}\Software\Classes",
            launcherExeName ?? GetLauncherExeName(),
            shellServerExeName ?? GetShellServerExeName(),
            onError);
    }
}
