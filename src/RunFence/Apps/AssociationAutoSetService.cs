using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Ipc;
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
    IHandlerMappingService handlerMappingService,
    IIpcCallerAuthorizer callerAuthorizer,
    ILoggingService log,
    AssociationRegistryWriter registryWriter,
    RegistryKey? hkuOverride = null,
    string? launcherPathOverride = null)
    : IAssociationAutoSetService
{
    private readonly RegistryKey _hku = hkuOverride ?? Registry.Users;

    private readonly HashSet<string> _cleanedSids = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _completedSids = new(StringComparer.OrdinalIgnoreCase);

    public void AutoSetForAllUsers()
    {
        _completedSids.Clear();

        var session = sessionProvider.GetSession();
        var filteredMappings = GetEffectiveAutoSetMappings(session);
        var filteredDirectMappings = GetEffectiveAutoSetDirectMappings(session);
        if (filteredMappings.Count == 0 && filteredDirectMappings.Count == 0)
            return;

        var launcherPath = filteredMappings.Count > 0 ? GetLauncherPath() : null;
        var database = session.Database;

        foreach (var sid in GetTargetUserSids(session))
        {
            var sidMappings = new Dictionary<string, HandlerMappingEntry>(filteredMappings, StringComparer.OrdinalIgnoreCase);
            var sidDirectMappings = new Dictionary<string, DirectHandlerEntry>(filteredDirectMappings, StringComparer.OrdinalIgnoreCase);
            ResolveConflictsForSid(sid, sidMappings, sidDirectMappings, database);
            AutoSetForUserInternal(sid, sidMappings, sidDirectMappings, launcherPath, session);
        }
    }

    public void AutoSetForUser(string sid)
    {
        if (SidResolutionHelper.IsSystemSid(sid)) return;

        var session = sessionProvider.GetSession();
        var filteredMappings = GetEffectiveAutoSetMappings(session);
        var filteredDirectMappings = GetEffectiveAutoSetDirectMappings(session);
        if (filteredMappings.Count == 0 && filteredDirectMappings.Count == 0)
            return;

        var launcherPath = filteredMappings.Count > 0 ? GetLauncherPath() : null;
        var database = session.Database;

        ResolveConflictsForSid(sid, filteredMappings, filteredDirectMappings, database);
        AutoSetForUserInternal(sid, filteredMappings, filteredDirectMappings, launcherPath, session);
    }

    public void ForceAutoSetForUser(string sid)
    {
        _completedSids.Remove(sid);
        AutoSetForUser(sid);
    }

    /// <remarks>
    /// Intentionally iterates all HKU subkeys: only restores keys that carry a
    /// <c>RunFenceFallback</c> registry marker, so non-managed SIDs are harmlessly skipped.
    /// </remarks>
    public void RestoreForAllUsers()
    {
        var credentialSids = sessionProvider.GetSession().CredentialStore.Credentials.Select(c => c.Sid).ToList();

        ForEachLoadedUserHive(credentialSids, sidSubKey =>
        {
            var classesPath = $@"{sidSubKey}\Software\Classes";
            using var classesKey = _hku.OpenSubKey(classesPath, writable: true);
            if (classesKey == null)
                return;

            // Find all keys with RunFenceFallback value and restore them
            foreach (var subKeyName in classesKey.GetSubKeyNames().ToList())
            {
                using var subKey = classesKey.OpenSubKey(subKeyName, writable: false);
                if (subKey?.GetValue(PathConstants.RunFenceFallbackValueName) != null)
                    registryWriter.RestoreKey(_hku, sidSubKey, subKeyName);
            }

            // Clean up stale per-user RunFence ProgIds (backward compat migration from per-user ProgIds to HKLM)
            registryWriter.CleanStalePerUserProgIds(classesKey, sidSubKey);
        });

        _cleanedSids.Clear();
        _completedSids.Clear();

        ShellNative.SHChangeNotify(ShellNative.SHCNE_ASSOCCHANGED, ShellNative.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

    public void RestoreKeyForAllUsers(string key)
    {
        var credentialSids = sessionProvider.GetSession().CredentialStore.Credentials.Select(c => c.Sid).ToList();

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

    private Dictionary<string, HandlerMappingEntry> GetEffectiveAutoSetMappings(SessionContext session)
    {
        var effectiveMappings = handlerMappingService.GetEffectiveHandlerMappings(session.Database);

        // DefaultAppsOnly associations (http, https, .htm, .html, .pdf, ftp) are excluded because
        // Windows ignores HKCU overrides for these — they can only be changed via Default Apps UI.
        // HKLM capabilities registration (AppHandlerRegistrationService) is sufficient to make
        // RunFence appear in Default Apps. Note: license enforcement is not applied here —
        // see class remarks for the rationale.
        return effectiveMappings
            .Where(kvp => !PathConstants.DefaultAppsOnlyAssociations.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, DirectHandlerEntry> GetEffectiveAutoSetDirectMappings(SessionContext session)
    {
        var directMappings = session.Database.Settings.DirectHandlerMappings;

        if (directMappings == null || directMappings.Count == 0)
            return new Dictionary<string, DirectHandlerEntry>(StringComparer.OrdinalIgnoreCase);

        return directMappings
            .Where(kvp => !PathConstants.DefaultAppsOnlyAssociations.Contains(kvp.Key))
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
    }

    private List<string> GetTargetUserSids(SessionContext session)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        if (!string.IsNullOrEmpty(interactiveSid))
            result.Add(interactiveSid);

        foreach (var credential in session.CredentialStore.Credentials)
        {
            if (!string.IsNullOrEmpty(credential.Sid))
                result.Add(credential.Sid);
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

    /// <summary>
    /// When both app-based and direct handlers are configured for the same key, resolves which
    /// wins for this SID. App-based wins when the account has explicit per-app IPC authorization;
    /// otherwise direct wins. Mutates both dictionaries in place.
    /// </summary>
    private void ResolveConflictsForSid(
        string sid,
        Dictionary<string, HandlerMappingEntry> appMappings,
        Dictionary<string, DirectHandlerEntry> directMappings,
        AppDatabase database)
    {
        foreach (var key in appMappings.Keys.Where(directMappings.ContainsKey).ToList())
        {
            var app = database.Apps.FirstOrDefault(a =>
                string.Equals(a.Id, appMappings[key].AppId, StringComparison.OrdinalIgnoreCase));
            if (app != null && callerAuthorizer.HasExplicitPerAppAuthorization(sid, app, database))
                directMappings.Remove(key); // app wins — explicit IPC override for this account
            else
                appMappings.Remove(key); // direct wins
        }
    }

    private void AutoSetForUserInternal(string sid, Dictionary<string, HandlerMappingEntry> mappings,
        Dictionary<string, DirectHandlerEntry> directMappings, string? launcherPath, SessionContext session)
    {
        if (session.Database.GetAccount(sid)?.ManageAssociations == false)
            return;

        if (_completedSids.Contains(sid))
            return;

        using var hiveHandle = hiveManager.EnsureHiveLoaded(sid);
        if (hiveHandle == null && !hiveManager.IsHiveLoaded(sid))
        {
            log.Warn($"AssociationAutoSetService: could not load hive for SID {sid}, skipping");
            return;
        }

        // Clean up stale per-user RunFence ProgIds (backward compat migration from per-user to HKLM)
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
                        registryWriter.AutoSetExtension(_hku, sid, key);
                    else
                        registryWriter.AutoSetProtocol(_hku, sid, key, launcherPath);
                }
                catch (Exception ex)
                {
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
                        registryWriter.AutoSetDirectClassExtension(_hku, sid, key, entry.ClassName);
                    else if (entry.Command != null)
                        registryWriter.AutoSetDirectCommandExtension(_hku, sid, key, entry.Command);
                }
                else
                {
                    if (entry.Command != null)
                        registryWriter.AutoSetDirectCommandProtocol(_hku, sid, key, entry.Command);
                    else
                        log.Warn($"AssociationAutoSetService: class-based protocol '{key}' is invalid, skipping");
                }
            }
            catch (Exception ex)
            {
                log.Warn($"AssociationAutoSetService: failed to auto-set direct handler '{key}' for {sid}: {ex.Message}");
            }
        }

        _completedSids.Add(sid);
        ShellNative.SHChangeNotify(ShellNative.SHCNE_ASSOCCHANGED, ShellNative.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
    }

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
}
