using System.Text.RegularExpressions;
using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.Apps;

/// <summary>
/// Registers per-association ProgIds for RunFence-managed file/URL handlers in the interactive
/// user's registry hive. All writes target HKU\&lt;interactive-user-SID&gt; (not the current admin's HKCU),
/// because RunFence runs elevated under a separate admin account.
/// </summary>
public class AppHandlerRegistrationService : IAppHandlerRegistrationService
{
    private readonly ILoggingService _log;
    private readonly ILicenseService _licenseService;
    private readonly RegistryKey _hku;
    private readonly string? _sidOverride;
    private readonly string? _launcherPathOverride;

    /// <summary>Browser-only associations always allowed in evaluation mode.</summary>
    private static readonly HashSet<string> BrowserAssociations =
        new(StringComparer.OrdinalIgnoreCase) { "http", "https", ".htm", ".html" };

    /// <summary>
    /// Characters allowed in association keys (alphanumeric, dot, dash, plus).
    /// The dot prefix distinguishes file extensions from URL protocols.
    /// </summary>
    private static readonly Regex SafeKeyPattern =
        new(@"^[a-zA-Z0-9.\-+]+$", RegexOptions.Compiled);

    public AppHandlerRegistrationService(
        ILoggingService log,
        ILicenseService licenseService,
        RegistryKey? hkuOverride = null,
        string? sidOverride = null,
        string? launcherPathOverride = null)
    {
        _log = log;
        _licenseService = licenseService;
        _hku = hkuOverride ?? Registry.Users;
        _sidOverride = sidOverride;
        _launcherPathOverride = launcherPathOverride;
    }

    private string? GetSid() => _sidOverride ?? SidResolutionHelper.GetInteractiveUserSid();

    public void Sync(Dictionary<string, string> effectiveHandlerMappings, List<AppEntry> apps)
    {
        var sid = GetSid();
        if (string.IsNullOrEmpty(sid))
        {
            _log.Warn("AppHandlerRegistrationService.Sync: interactive user SID unavailable, skipping");
            return;
        }

        var launcherPath = _launcherPathOverride ?? Path.Combine(AppContext.BaseDirectory, Constants.LauncherExeName);
        if (!File.Exists(launcherPath))
        {
            _log.Warn($"AppHandlerRegistrationService.Sync: launcher not found at {launcherPath}, skipping");
            return;
        }

        // Apply evaluation limit: only browser associations allowed when unlicensed
        var mappings = FilterForLicense(effectiveHandlerMappings);

        // Validate keys; skip unsafe ones
        var validMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in mappings)
        {
            if (!IsValidKey(kvp.Key))
            {
                _log.Warn($"AppHandlerRegistrationService.Sync: skipping association key '{kvp.Key}' (contains unsafe characters)");
                continue;
            }

            var appEntry = apps.FirstOrDefault(a => a.Id == kvp.Value);
            if (appEntry == null)
            {
                _log.Warn($"AppHandlerRegistrationService.Sync: skipping association '{kvp.Key}' → '{kvp.Value}' (app not found)");
                continue;
            }

            validMappings[kvp.Key] = kvp.Value;
        }

        _log.Info($"AppHandlerRegistrationService.Sync: registering {validMappings.Count} association(s) for user {sid}");

        if (validMappings.Count == 0)
        {
            // No valid mappings — remove everything to avoid stale registry entries
            UnregisterAll();
            return;
        }

        try
        {
            // Remove stale ProgIds (RunFence_* subkeys not in current valid mappings)
            RemoveStaleProgIds(sid, validMappings);

            // Ensure parent Capabilities key exists
            EnsureParentCapabilities(sid, launcherPath);

            // Clear and rebuild URLAssociations and FileAssociations
            RebuildCapabilities(sid, validMappings);

            // Create/update per-association ProgIds
            foreach (var (association, appId) in validMappings)
            {
                RegisterAssociationProgId(sid, association, apps.First(a => a.Id == appId), launcherPath);
            }

            NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            _log.Info("AppHandlerRegistrationService.Sync: complete");
        }
        catch (Exception ex)
        {
            _log.Error("AppHandlerRegistrationService.Sync: failed", ex);
        }
    }

    public void UnregisterAll()
    {
        var sid = GetSid();
        if (string.IsNullOrEmpty(sid))
        {
            _log.Warn("AppHandlerRegistrationService.UnregisterAll: interactive user SID unavailable, skipping");
            return;
        }

        _log.Info($"AppHandlerRegistrationService.UnregisterAll: removing all handler registrations for user {sid}");

        try
        {
            // Delete all RunFence_* ProgIds under Software\Classes
            using var classesKey = _hku.OpenSubKey($@"{sid}\Software\Classes", writable: true);
            if (classesKey != null)
            {
                var subKeyNames = classesKey.GetSubKeyNames()
                    .Where(n => n.StartsWith(Constants.HandlerProgIdPrefix, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                foreach (var name in subKeyNames)
                {
                    try
                    {
                        classesKey.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
                    }
                    catch (Exception ex)
                    {
                        _log.Warn($"Failed to delete ProgId '{name}': {ex.Message}");
                    }
                }
            }

            // Remove RegisteredApplications entry
            using var regAppsKey = _hku.OpenSubKey($@"{sid}\Software\RegisteredApplications", writable: true);
            regAppsKey?.DeleteValue(Constants.HandlerRegisteredAppName, throwOnMissingValue: false);

            NativeMethods.SHChangeNotify(NativeMethods.SHCNE_ASSOCCHANGED, NativeMethods.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            _log.Info("AppHandlerRegistrationService.UnregisterAll: complete");
        }
        catch (Exception ex)
        {
            _log.Error("AppHandlerRegistrationService.UnregisterAll: failed", ex);
        }
    }

    private void RegisterAssociationProgId(string sid, string association, AppEntry app, string launcherPath)
    {
        var progId = Constants.HandlerProgIdPrefix + association;
        var classesPrefix = $@"{sid}\Software\Classes\{progId}";

        // Determine icon: use cached icon file if available, otherwise app exe, otherwise launcher
        var iconPath = ResolveIconPath(app, launcherPath);

        using (var rootKey = _hku.CreateSubKey(classesPrefix))
            rootKey.SetValue(null, progId);

        using (var iconKey = _hku.CreateSubKey($@"{classesPrefix}\DefaultIcon"))
            iconKey.SetValue(null, $"\"{iconPath}\",0");

        // Shell command uses unquoted %1 — Launcher extracts it via CommandLineHelper.SkipArgs
        using (var commandKey = _hku.CreateSubKey($@"{classesPrefix}\shell\open\command"))
            commandKey.SetValue(null, $"\"{launcherPath}\" --resolve \"{association}\" %1");
    }

    private void EnsureParentCapabilities(string sid, string launcherPath)
    {
        var parentPrefix = $@"{sid}\Software\Classes\{Constants.HandlerParentKey}";

        using (var rootKey = _hku.CreateSubKey(parentPrefix))
            rootKey.SetValue(null, Constants.HandlerRegisteredAppName);

        using (var iconKey = _hku.CreateSubKey($@"{parentPrefix}\DefaultIcon"))
            iconKey.SetValue(null, $"\"{launcherPath}\",0");

        using (var capsKey = _hku.CreateSubKey($@"{parentPrefix}\Capabilities"))
        {
            capsKey.SetValue("ApplicationName", Constants.HandlerRegisteredAppName);
            capsKey.SetValue("ApplicationDescription", "Launches files and URLs through RunFence-managed applications");
        }

        using var regAppsKey = _hku.CreateSubKey($@"{sid}\Software\RegisteredApplications");
        regAppsKey.SetValue(Constants.HandlerRegisteredAppName,
            $@"Software\Classes\{Constants.HandlerParentKey}\Capabilities");
    }

    private void RebuildCapabilities(string sid, Dictionary<string, string> validMappings)
    {
        var capsPrefix = $@"{sid}\Software\Classes\{Constants.HandlerParentKey}\Capabilities";

        // Clear existing URLAssociations and FileAssociations subkeys before rebuilding
        try
        {
            _hku.DeleteSubKeyTree($@"{capsPrefix}\URLAssociations", throwOnMissingSubKey: false);
        }
        catch
        {
        }

        try
        {
            _hku.DeleteSubKeyTree($@"{capsPrefix}\FileAssociations", throwOnMissingSubKey: false);
        }
        catch
        {
        }

        if (validMappings.Count == 0)
            return;

        // Separate file extensions (start with '.') from URL protocols
        var urlAssociations = validMappings.Where(kvp => !kvp.Key.StartsWith('.'))
            .ToDictionary(kvp => kvp.Key, kvp => Constants.HandlerProgIdPrefix + kvp.Key);
        var fileAssociations = validMappings.Where(kvp => kvp.Key.StartsWith('.'))
            .ToDictionary(kvp => kvp.Key, kvp => Constants.HandlerProgIdPrefix + kvp.Key);

        if (urlAssociations.Count > 0)
        {
            using var urlKey = _hku.CreateSubKey($@"{capsPrefix}\URLAssociations");
            foreach (var kvp in urlAssociations)
                urlKey.SetValue(kvp.Key, kvp.Value);
        }

        if (fileAssociations.Count > 0)
        {
            using var fileKey = _hku.CreateSubKey($@"{capsPrefix}\FileAssociations");
            foreach (var kvp in fileAssociations)
                fileKey.SetValue(kvp.Key, kvp.Value);
        }
    }

    private void RemoveStaleProgIds(string sid, Dictionary<string, string> validMappings)
    {
        var desiredProgIds = new HashSet<string>(
            validMappings.Keys.Select(k => Constants.HandlerProgIdPrefix + k),
            StringComparer.OrdinalIgnoreCase) {
            // Also keep the parent key
            Constants.HandlerParentKey };

        using var classesKey = _hku.OpenSubKey($@"{sid}\Software\Classes", writable: true);
        if (classesKey == null)
            return;

        var toDelete = classesKey.GetSubKeyNames()
            .Where(n => n.StartsWith(Constants.HandlerProgIdPrefix, StringComparison.OrdinalIgnoreCase)
                        && !desiredProgIds.Contains(n))
            .ToList();

        foreach (var name in toDelete)
        {
            try
            {
                classesKey.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
            }
            catch (Exception ex)
            {
                _log.Warn($"Failed to remove stale ProgId '{name}': {ex.Message}");
            }
        }
    }

    private static string ResolveIconPath(AppEntry app, string launcherPath)
    {
        // Use the app's own exe as icon source (original icon without any badge overlay)
        if (!string.IsNullOrEmpty(app.ExePath) && File.Exists(app.ExePath))
            return app.ExePath;

        return launcherPath;
    }

    private Dictionary<string, string> FilterForLicense(Dictionary<string, string> mappings)
    {
        if (_licenseService.IsLicensed)
            return mappings;

        // Evaluation mode: only browser associations allowed
        var filtered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in mappings)
        {
            if (BrowserAssociations.Contains(kvp.Key))
                filtered[kvp.Key] = kvp.Value;
        }

        return filtered;
    }

    /// <summary>
    /// Common association keys shown as suggestions in the UI, excluding the "Browser" convenience token.
    /// </summary>
    public static readonly string[] CommonAssociationSuggestions =
    [
        "http", "https", "mailto", "ftp",
        ".htm", ".html", ".pdf", ".txt",
        ".jpg", ".png", ".svg", ".xml", ".json"
    ];

    public static bool IsValidKey(string key)
    {
        return !string.IsNullOrEmpty(key) && SafeKeyPattern.IsMatch(key);
    }
}