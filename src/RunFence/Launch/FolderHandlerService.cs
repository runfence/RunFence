using System.DirectoryServices.AccountManagement;
using Microsoft.Win32;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Launch;

public class FolderHandlerService(
    ILoggingService log,
    IPermissionGrantService permissionGrantService,
    RegistryKey? hkuOverride = null,
    string? launcherPathOverride = null,
    Func<string, bool>? isAdminAccount = null,
    string? shellServerPathOverride = null)
    : IFolderHandlerService
{
    private readonly RegistryKey _hku = hkuOverride ?? Registry.Users;
    private readonly Func<string, bool> _isAdminAccount = isAdminAccount ?? DefaultIsAdminAccount;

    private readonly HashSet<string> _registeredSids =
        new(StringComparer.OrdinalIgnoreCase);

    public bool IsRegistered(string accountSid)
        => _registeredSids.Contains(accountSid);

    public void Register(string accountSid)
    {
        if (_registeredSids.Contains(accountSid))
        {
            log.Info($"FolderHandlerService: already registered for {accountSid}, skipping");
            return;
        }

        if (string.Equals(accountSid, SidResolutionHelper.GetInteractiveUserSid(),
                StringComparison.OrdinalIgnoreCase))
        {
            log.Info($"FolderHandlerService: skipping registration for interactive user {accountSid}");
            return;
        }

        if (_isAdminAccount(accountSid))
        {
            log.Info($"FolderHandlerService: skipping registration for admin account {accountSid}");
            return;
        }

        // No IPC authorization check here by design: opening a folder in Explorer grants no elevated access.
        // Path safety is enforced in IpcOpenFolderHandler via IDirectoryValidator (including TOCTOU protection).
        // Restricting registration to IPC-authorized accounts would silently break the "Show in Folder"
        // feature for accounts not explicitly listed as IPC callers.
        var launcherPath = launcherPathOverride
                           ?? Path.Combine(AppContext.BaseDirectory, Constants.LauncherExeName);
        if (!File.Exists(launcherPath))
        {
            log.Warn($"FolderHandlerService: launcher not found at {launcherPath}, skipping registration");
            return;
        }

        log.Info($"FolderHandlerService: registering folder handler for {accountSid}");
        try
        {
            var commandValue = $"\"{launcherPath}\" --open-folder \"%V\"";

            // Register "open" and "explore" verbs under Directory so we intercept both
            // ShellExecute("open", dir) and ShellExecute("explore", dir) calls.
            // Also set the Directory\shell default verb to "open" so that ShellExecute with
            // a NULL verb (used by Firefox's fallback after SHOpenFolderAndSelectItems fails)
            // resolves to our handler instead of falling through to Windows internal Explorer.
            // (HKLM sets Directory\shell default to "none" with no matching subkey.)
            SetCommandValue(accountSid, @"Directory\shell\open\command", commandValue);
            SetCommandValue(accountSid, @"Directory\shell\explore\command", commandValue);
            SetDirectoryShellDefaultVerb(accountSid, "open");
            SetFolderCommandValue(accountSid, commandValue);

            // Ensure the RunAs account can execute the launcher.
            permissionGrantService.EnsureExeDirectoryAccess(launcherPath, accountSid);

            // Schedule cleanup via RunOnce so if the account ever logs in interactively,
            // it removes the handler from its own HKCU on first logon.
            SetRunOnce(accountSid, launcherPath);

            NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST,
                IntPtr.Zero, IntPtr.Zero);
            _registeredSids.Add(accountSid);
            log.Info($"FolderHandlerService: registration complete for {accountSid}");
        }
        catch (Exception ex)
        {
            log.Error($"FolderHandlerService: registration failed for {accountSid}", ex);
        }
    }

    public void Unregister(string accountSid)
    {
        log.Info($"FolderHandlerService: unregistering folder handler for {accountSid}");
        try
        {
            _hku.DeleteSubKeyTree($@"{accountSid}\Software\Classes\Directory\shell\open",
                throwOnMissingSubKey: false);
            _hku.DeleteSubKeyTree($@"{accountSid}\Software\Classes\Directory\shell\explore",
                throwOnMissingSubKey: false);
            RemoveDirectoryShellDefaultVerb(accountSid);
            _hku.DeleteSubKeyTree($@"{accountSid}\Software\Classes\Folder\shell\open",
                throwOnMissingSubKey: false);
            RemoveRunOnce(accountSid);

            _registeredSids.Remove(accountSid);
            NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST,
                IntPtr.Zero, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            log.Error($"FolderHandlerService: unregistration failed for {accountSid}", ex);
        }
    }

    public void UnregisterAll()
    {
        foreach (var sid in _registeredSids.ToList())
            Unregister(sid);
    }

    /// <summary>
    /// Removes any stale folder/directory handler registrations and CLSID overrides left by a
    /// prior crash or approach change. Enumerates all loaded HKU hives.
    /// Called once at startup before any app launches.
    /// </summary>
    public void CleanupStaleRegistrations()
    {
        log.Info("FolderHandlerService: cleaning up stale registrations.");
        try
        {
            var launcherExeName = Path.GetFileName(
                launcherPathOverride ?? Path.Combine(AppContext.BaseDirectory, Constants.LauncherExeName));
            var shellServerExeName = Path.GetFileName(
                shellServerPathOverride ?? Path.Combine(AppContext.BaseDirectory, Constants.ShellServerExeName));

            foreach (var sidName in _hku.GetSubKeyNames())
                CleanupStaleForSid(sidName, launcherExeName, shellServerExeName);
        }
        catch (Exception ex)
        {
            log.Warn($"FolderHandlerService: cleanup failed: {ex.Message}");
        }
    }

    private void CleanupStaleForSid(string sidName, string launcherExeName, string shellServerExeName)
    {
        bool cleaned = false;
        bool dirOpenCleaned = TryDeleteStaleCommandKey(sidName, @"Software\Classes\Directory\shell\open\command", launcherExeName);
        if (dirOpenCleaned)
            RemoveDirectoryShellDefaultVerb(sidName);
        cleaned |= dirOpenCleaned;
        cleaned |= TryDeleteStaleCommandKey(sidName, @"Software\Classes\Directory\shell\explore\command", launcherExeName);
        cleaned |= TryDeleteStaleCommandKey(sidName, @"Software\Classes\Folder\shell\open\command", launcherExeName);
        cleaned |= TryDeleteStaleClsidOverride(sidName, shellServerExeName, launcherExeName);
        if (cleaned)
        {
            log.Info($"FolderHandlerService: removed stale registration for {sidName}");
            NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST,
                IntPtr.Zero, IntPtr.Zero);
        }
    }

    private bool TryDeleteStaleCommandKey(string sidName, string subKeyPath, string launcherExeName)
    {
        try
        {
            using var key = _hku.OpenSubKey($@"{sidName}\{subKeyPath}");
            if (key?.GetValue(null) is not string value)
                return false;
            if (!value.Contains(launcherExeName, StringComparison.OrdinalIgnoreCase))
                return false;

            var parentPath = subKeyPath[..subKeyPath.LastIndexOf('\\')];
            _hku.DeleteSubKeyTree($@"{sidName}\{parentPath}", throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception ex)
        {
            log.Warn($"FolderHandlerService: failed to delete stale command key {sidName}\\{subKeyPath}: {ex.Message}");
            return false;
        }
    }

    private bool TryDeleteStaleClsidOverride(string sidName, string shellServerExeName, string launcherExeName)
    {
        try
        {
            using var key = _hku.OpenSubKey(
                $@"{sidName}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}\LocalServer32");
            if (key?.GetValue(null) is not string value)
                return false;
            if (!value.Contains(shellServerExeName, StringComparison.OrdinalIgnoreCase) &&
                !value.Contains(launcherExeName, StringComparison.OrdinalIgnoreCase))
                return false;

            _hku.DeleteSubKeyTree(
                $@"{sidName}\{FolderHandlerNative.ShellWindowsClsidRegistryPath}",
                throwOnMissingSubKey: false);
            return true;
        }
        catch (Exception ex)
        {
            log.Warn($"FolderHandlerService: failed to delete stale CLSID override for {sidName}: {ex.Message}");
            return false;
        }
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
        key.SetValue(null, verb);
    }

    private void RemoveDirectoryShellDefaultVerb(string accountSid)
    {
        try
        {
            using var key = _hku.OpenSubKey($@"{accountSid}\Software\Classes\Directory\shell",
                writable: true);
            key?.DeleteValue("", throwOnMissingValue: false);
        }
        catch
        {
        }
    }

    // Folder\shell\open\command in HKLM has a DelegateExecute value that causes the shell to
    // use COM activation (ExplorerFrame.dll) instead of the command string, even when HKCU
    // provides a command. Shadow it with an empty string so the shell falls through to our command.
    private void SetFolderCommandValue(string accountSid, string commandValue)
    {
        var fullPath = $@"{accountSid}\Software\Classes\Folder\shell\open\command";
        using var key = _hku.CreateSubKey(fullPath)
                        ?? throw new InvalidOperationException($"Failed to create registry key: {fullPath}");
        key.SetValue(null, commandValue);
        key.SetValue("DelegateExecute", "");
    }

    private void SetRunOnce(string accountSid, string launcherPath)
    {
        var scriptPath = Path.Combine(Path.GetDirectoryName(launcherPath)!, Constants.FolderHandlerUnregisterScriptName);
        if (!File.Exists(scriptPath))
        {
            log.Warn($"FolderHandlerService: unregister script not found at {scriptPath}, skipping RunOnce");
            return;
        }

        var fullPath = $@"{accountSid}\Software\Microsoft\Windows\CurrentVersion\RunOnce";
        using var key = _hku.CreateSubKey(fullPath)
                        ?? throw new InvalidOperationException($"Failed to create registry key: {fullPath}");
        // cmd /c ""path"" — outer quotes required by cmd.exe when the argument starts with a quote
        key.SetValue(Constants.FolderHandlerRunOnceValueName, $"cmd /c \"\"{scriptPath}\"\"");
    }

    private void RemoveRunOnce(string accountSid)
    {
        try
        {
            using var key = _hku.OpenSubKey(
                $@"{accountSid}\Software\Microsoft\Windows\CurrentVersion\RunOnce", writable: true);
            key?.DeleteValue(Constants.FolderHandlerRunOnceValueName, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            log.Warn($"FolderHandlerService: failed to remove RunOnce for {accountSid}: {ex.Message}");
        }
    }

    private static bool DefaultIsAdminAccount(string accountSid)
    {
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var principal = Principal.FindByIdentity(context, IdentityType.Sid, accountSid);
            if (principal == null)
                return false;
            using var adminGroup = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, "S-1-5-32-544");
            if (adminGroup == null)
                return false;
            return principal.IsMemberOf(adminGroup);
        }
        catch
        {
            return false;
        }
    }
}