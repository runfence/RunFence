using Microsoft.Win32;
using System.ComponentModel;
using System.Security;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Apps;

/// <summary>
/// Auto-registers RunFence as the HKCU handler for all configured associations for each
/// target user (interactive + credential users). Stores the original handler in a
/// <c>RunFenceFallback</c> registry value so the original can be restored on cleanup.
/// </summary>
/// <remarks>
/// This service runs in the foundation (pre-session) DI scope so that <see cref="ProcessLauncher"/>
/// can trigger it lazily on first launch. <c>ILicenseService</c> is session-scoped and therefore
/// cannot be injected here. License enforcement is applied upstream: <see cref="AppHandlerRegistrationService"/>
/// filters HKLM ProgIds by license, so unlicensed non-browser extensions fail at the ProgId level (safe).
/// Protocol associations could theoretically bypass evaluation restrictions, but this is acceptable
/// in eval mode. <see cref="PathConstants.DefaultAppsOnlyAssociations"/> are always skipped regardless of license.
/// </remarks>
public class AssociationAutoSetService(
    IUserHiveManager hiveManager,
    ISessionProvider sessionProvider,
    Func<IUiThreadInvoker> uiThreadInvokerFactory,
    Func<IHandlerMappingService> handlerMappingService,
    IAssociationPolicyService associationPolicyService,
    ILoggingService log,
    AssociationRegistryWriter registryWriter,
    RegistryKey? hkuOverride = null,
    string? launcherPathOverride = null)
    : IAssociationAutoSetService
{
    private readonly RegistryKey _hku = hkuOverride ?? Registry.Users;
    private IHandlerMappingService HandlerMappingService => handlerMappingService();

    private readonly HashSet<string> _cleanedSids = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _completedSids = new(StringComparer.OrdinalIgnoreCase);

    public AssociationAutoSetResult AutoSetForAllUsers()
    {
        _completedSids.Clear();

        var snapshot = CaptureAutoSetSnapshot();
        var filteredMappings = snapshot.HandlerMappings;
        var filteredDirectMappings = snapshot.DirectHandlerMappings;
        if (filteredMappings.Count == 0 && filteredDirectMappings.Count == 0)
            return AssociationAutoSetResult.Success;

        var launcherPath = filteredMappings.Count > 0 ? GetLauncherPath() : null;
        var database = snapshot.Database;
        var warnings = new List<string>();

        foreach (var sid in snapshot.TargetUserSids)
        {
            var sidMappings = new Dictionary<string, HandlerMappingEntry>(filteredMappings, StringComparer.OrdinalIgnoreCase);
            var sidDirectMappings = new Dictionary<string, DirectHandlerEntry>(filteredDirectMappings, StringComparer.OrdinalIgnoreCase);
            associationPolicyService.ResolveConflictsForSid(sid, sidMappings, sidDirectMappings, database);
            warnings.AddRange(AutoSetForUserInternal(sid, sidMappings, sidDirectMappings, launcherPath, database));
        }

        return CreateResult(warnings);
    }

    public AssociationAutoSetResult AutoSetForUser(string sid)
    {
        if (SidResolutionHelper.IsSystemSid(sid))
            return AssociationAutoSetResult.Success;

        var snapshot = CaptureAutoSetSnapshot();
        var filteredMappings = snapshot.HandlerMappings;
        var filteredDirectMappings = snapshot.DirectHandlerMappings;
        if (filteredMappings.Count == 0 && filteredDirectMappings.Count == 0)
            return AssociationAutoSetResult.Success;

        var launcherPath = filteredMappings.Count > 0 ? GetLauncherPath() : null;
        var database = snapshot.Database;
        associationPolicyService.ResolveConflictsForSid(sid, filteredMappings, filteredDirectMappings, database);

        var warnings = AutoSetForUserInternal(
            sid,
            filteredMappings,
            filteredDirectMappings,
            launcherPath,
            database);
        return CreateResult(warnings);
    }

    public AssociationAutoSetResult ForceAutoSetForUser(string sid)
    {
        _completedSids.Remove(sid);
        return AutoSetForUser(sid);
    }

    /// <remarks>
    /// Intentionally iterates all HKU subkeys: only restores keys that carry a
    /// <c>RunFenceFallback</c> registry marker, so non-managed SIDs are harmlessly skipped.
    /// </remarks>
    public void RestoreForAllUsers()
    {
        var credentialSids = CaptureCredentialSids();

        ForEachLoadedUserHive(credentialSids, sidSubKey =>
        {
            var classesPath = $@"{sidSubKey}\Software\Classes";
            using var classesKey = _hku.OpenSubKey(classesPath, writable: true);
            if (classesKey == null)
                return;

            foreach (var subKeyName in classesKey.GetSubKeyNames().ToList())
            {
                using var subKey = classesKey.OpenSubKey(subKeyName, writable: false);
                if (subKey?.GetValue(PathConstants.RunFenceFallbackValueName) != null)
                    registryWriter.RestoreKey(_hku, sidSubKey, subKeyName);
            }

            registryWriter.CleanStalePerUserProgIds(classesKey, sidSubKey);
        });

        _cleanedSids.Clear();
        _completedSids.Clear();

        ShellNative.SHChangeNotify(ShellNative.SHCNE_ASSOCCHANGED, ShellNative.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    public void RestoreKeyForAllUsers(string key)
    {
        var credentialSids = CaptureCredentialSids();

        ForEachLoadedUserHive(credentialSids, sidSubKey =>
        {
            registryWriter.RestoreKey(_hku, sidSubKey, key);
        });

        ShellNative.SHChangeNotify(ShellNative.SHCNE_ASSOCCHANGED, ShellNative.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    public void RestoreForUser(string sid)
    {
        _completedSids.Remove(sid);

        using var hiveHandle = hiveManager.EnsureHiveLoaded(sid);
        if (hiveHandle == null && !hiveManager.IsHiveLoaded(sid))
        {
            log.Warn($"AssociationAutoSetService: could not load hive for SID {sid}, skipping restore");
            return;
        }

        var classesPath = $@"{sid}\Software\Classes";
        using var classesKey = _hku.OpenSubKey(classesPath, writable: false);
        if (classesKey == null)
            return;

        foreach (var subKeyName in classesKey.GetSubKeyNames().ToList())
        {
            using var subKey = classesKey.OpenSubKey(subKeyName, writable: false);
            if (subKey?.GetValue(PathConstants.RunFenceFallbackValueName) != null)
                registryWriter.RestoreKey(_hku, sid, subKeyName);
        }

        ShellNative.SHChangeNotify(ShellNative.SHCNE_ASSOCCHANGED, ShellNative.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    private Dictionary<string, HandlerMappingEntry> GetEffectiveAutoSetMappings(AppDatabase databaseSnapshot)
    {
        var effectiveMappings = HandlerMappingService.GetEffectiveHandlerMappings(databaseSnapshot);
        return effectiveMappings
            .Where(kvp => !PathConstants.DefaultAppsOnlyAssociations.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, DirectHandlerEntry> GetEffectiveAutoSetDirectMappings(AppDatabase databaseSnapshot)
    {
        var directMappings = databaseSnapshot.Settings.DirectHandlerMappings;

        if (directMappings == null || directMappings.Count == 0)
            return new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase);

        return directMappings
            .Where(kvp => !PathConstants.DefaultAppsOnlyAssociations.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> GetTargetUserSids(IEnumerable<string?> credentialSids)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        if (!string.IsNullOrEmpty(interactiveSid))
            result.Add(interactiveSid);

        foreach (var sid in credentialSids)
        {
            if (!string.IsNullOrEmpty(sid))
                result.Add(sid);
        }

        var currentSid = SidResolutionHelper.GetCurrentUserSid();
        if (!string.IsNullOrEmpty(currentSid))
            result.Remove(currentSid);

        return result.ToList();
    }

    private string? GetLauncherPath()
    {
        var launcherPath = launcherPathOverride ?? Path.Combine(AppContext.BaseDirectory, PathConstants.LauncherExeName);
        if (!File.Exists(launcherPath))
        {
            log.Warn($"AssociationAutoSetService: launcher not found at {launcherPath}");
            return null;
        }

        return launcherPath;
    }

    private IReadOnlyList<string> AutoSetForUserInternal(
        string sid,
        Dictionary<string, HandlerMappingEntry> mappings,
        Dictionary<string, DirectHandlerEntry> directMappings,
        string? launcherPath,
        AppDatabase databaseSnapshot)
    {
        var warnings = new List<string>();

        if (databaseSnapshot.GetAccount(sid)?.ManageAssociations == false)
            return warnings;

        if (_completedSids.Contains(sid))
            return warnings;

        using var hiveHandle = hiveManager.EnsureHiveLoaded(sid);
        if (hiveHandle == null && !hiveManager.IsHiveLoaded(sid))
        {
            log.Warn($"AssociationAutoSetService: could not load hive for SID {sid}, skipping");
            return warnings;
        }

        if (!_cleanedSids.Contains(sid))
        {
            var classesPath = $@"{sid}\Software\Classes";
            using var classesKey = _hku.OpenSubKey(classesPath, writable: true);
            if (classesKey != null)
                registryWriter.CleanStalePerUserProgIds(classesKey, sid);
            _cleanedSids.Add(sid);
        }

        if (launcherPath != null)
        {
            foreach (var (key, _) in mappings)
            {
                if (!AppHandlerRegistrationService.IsValidKey(key))
                    continue;

                try
                {
                    if (key.StartsWith('.'))
                    {
                        if (!NeedsExtensionAutoSet(sid, key))
                            continue;

                        registryWriter.AutoSetExtension(_hku, sid, key);
                    }
                    else
                    {
                        if (!NeedsProtocolAutoSet(sid, key, launcherPath))
                            continue;

                        registryWriter.AutoSetProtocol(_hku, sid, key, launcherPath);
                    }
                }
                catch (Exception ex)
                {
                    if (TryCreateExpectedAccessDeniedWarning(sid, key, ex, out var warning))
                    {
                        warnings.Add(warning);
                        log.Warn(warning);
                        continue;
                    }

                    log.Warn($"AssociationAutoSetService: failed to auto-set '{key}' for {sid}: {ex.Message}");
                }
            }
        }

        foreach (var (key, entry) in directMappings)
        {
            if (!AppHandlerRegistrationService.IsValidKey(key))
                continue;

            try
            {
                    if (key.StartsWith('.'))
                    {
                        if (entry.ClassName != null)
                        {
                            if (!NeedsDirectClassExtensionAutoSet(sid, key, entry.ClassName))
                                continue;

                            registryWriter.AutoSetDirectClassExtension(_hku, sid, key, entry.ClassName);
                        }
                        else if (entry.Command != null)
                        {
                            if (!NeedsDirectCommandExtensionAutoSet(sid, key, entry.Command))
                                continue;

                            registryWriter.AutoSetDirectCommandExtension(_hku, sid, key, entry.Command);
                        }
                    }
                    else
                    {
                        if (entry.Command != null)
                        {
                            if (!NeedsDirectCommandProtocolAutoSet(sid, key, entry.Command))
                                continue;

                            registryWriter.AutoSetDirectCommandProtocol(_hku, sid, key, entry.Command);
                        }
                        else
                    {
                        log.Warn($"AssociationAutoSetService: class-based protocol '{key}' is invalid, skipping");
                    }
                }
            }
            catch (Exception ex)
            {
                if (TryCreateExpectedAccessDeniedWarning(sid, key, ex, out var warning))
                {
                    warnings.Add(warning);
                    log.Warn(warning);
                    continue;
                }

                log.Warn($"AssociationAutoSetService: failed to auto-set direct handler '{key}' for {sid}: {ex.Message}");
            }
        }

        _completedSids.Add(sid);
        ShellNative.SHChangeNotify(ShellNative.SHCNE_ASSOCCHANGED, ShellNative.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        return warnings;
    }

    private AutoSetSnapshot CaptureAutoSetSnapshot()
        => uiThreadInvokerFactory().Invoke(() =>
        {
            var session = sessionProvider.GetSession();
            var databaseSnapshot = session.Database.CreateSnapshot();
            var credentialSids = session.CredentialStore.Credentials
                .Select(c => c.Sid)
                .OfType<string>()
                .ToList();
            return new AutoSetSnapshot(
                databaseSnapshot,
                GetEffectiveAutoSetMappings(databaseSnapshot),
                GetEffectiveAutoSetDirectMappings(databaseSnapshot),
                GetTargetUserSids(credentialSids));
        });

    private List<string> CaptureCredentialSids()
        => uiThreadInvokerFactory().Invoke(() =>
            sessionProvider.GetSession().CredentialStore.Credentials
                .Select(c => c.Sid)
                .OfType<string>()
                .ToList());

    /// <summary>
    /// Loads hives for all credential SIDs, iterates all target SID subkeys in HKU,
    /// invokes <paramref name="perSidAction"/> for each, then disposes the hive handles.
    /// Callers are responsible for calling <see cref="ShellNative.SHChangeNotify"/> after.
    /// </summary>
    private void ForEachLoadedUserHive(IEnumerable<string> credentialSids, Action<string> perSidAction)
    {
        var hiveHandles = new List<IDisposable?>();
        try
        {
            foreach (var sid in credentialSids)
                hiveHandles.Add(hiveManager.EnsureHiveLoaded(sid));

            foreach (var sidSubKey in _hku.GetSubKeyNames().Where(AssociationRegistryWriter.IsTargetSidSubKey))
                perSidAction(sidSubKey);
        }
        finally
        {
            foreach (var handle in hiveHandles)
                handle?.Dispose();
        }
    }

    private bool NeedsExtensionAutoSet(string sid, string key)
    {
        using var extKey = _hku.OpenSubKey($@"{sid}\Software\Classes\{key}");
        var currentDefault = extKey?.GetValue(null) as string;
        var expectedProgId = PathConstants.HandlerProgIdPrefix + key;
        return !string.Equals(currentDefault, expectedProgId, StringComparison.OrdinalIgnoreCase);
    }

    private bool NeedsProtocolAutoSet(string sid, string key, string launcherPath)
    {
        using var commandKey = _hku.OpenSubKey($@"{sid}\Software\Classes\{key}\shell\open\command");
        var currentCommand = commandKey?.GetValue(null) as string;
        var expectedCommand = $"\"{launcherPath}\" --resolve \"{key}\" %1";
        return !string.Equals(currentCommand, expectedCommand, StringComparison.OrdinalIgnoreCase);
    }

    private bool NeedsDirectClassExtensionAutoSet(string sid, string key, string className)
    {
        using var extKey = _hku.OpenSubKey($@"{sid}\Software\Classes\{key}");
        var currentDefault = extKey?.GetValue(null) as string;
        return !string.Equals(currentDefault, className, StringComparison.OrdinalIgnoreCase);
    }

    private bool NeedsDirectCommandExtensionAutoSet(string sid, string key, string command)
    {
        using var commandKey = _hku.OpenSubKey($@"{sid}\Software\Classes\{key}\shell\open\command");
        var currentCommand = commandKey?.GetValue(null) as string;
        return !string.Equals(currentCommand, command, StringComparison.OrdinalIgnoreCase);
    }

    private bool NeedsDirectCommandProtocolAutoSet(string sid, string key, string command)
        => NeedsDirectCommandExtensionAutoSet(sid, key, command);

    private static AssociationAutoSetResult CreateResult(IReadOnlyList<string> warnings)
        => warnings.Count == 0
            ? AssociationAutoSetResult.Success
            : new AssociationAutoSetResult(AssociationAutoSetStatus.SucceededWithWarnings, warnings);

    private static bool TryCreateExpectedAccessDeniedWarning(string sid, string key, Exception ex, out string warning)
    {
        if (!IsExpectedAccessDenied(ex))
        {
            warning = string.Empty;
            return false;
        }

        warning = $"AssociationAutoSetService: access denied while auto-setting '{key}' for {sid}; keeping ManageAssociations enabled.";
        return true;
    }

    private static bool IsExpectedAccessDenied(Exception ex)
        => ex is UnauthorizedAccessException
           or SecurityException
           or Win32Exception { NativeErrorCode: 5 };

    private sealed record AutoSetSnapshot(
        AppDatabase Database,
        Dictionary<string, HandlerMappingEntry> HandlerMappings,
        Dictionary<string, DirectHandlerEntry> DirectHandlerMappings,
        List<string> TargetUserSids);

}
