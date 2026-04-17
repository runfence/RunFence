using System.Text.RegularExpressions;
using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.Apps;

/// <summary>
/// Registers per-association ProgIds for RunFence-managed file/URL handlers in HKLM.
/// ProgIds go under HKLM\Software\Classes; Capabilities go under HKLM\Software\RunFence\Capabilities
/// (the standard location — keeping Capabilities outside Software\Classes prevents Windows from
/// double-discovering RunFence in Default Apps).
/// </summary>
public class AppHandlerRegistrationService(
    ILoggingService log,
    ILicenseService licenseService,
    RegistryKey? hklmOverride = null,
    string? launcherPathOverride = null)
    : IAppHandlerRegistrationService
{
    private readonly RegistryKey _hklm = hklmOverride ?? Registry.LocalMachine;

    /// <summary>
    /// Characters allowed in association keys (alphanumeric, dot, dash, plus).
    /// The dot prefix distinguishes file extensions from URL protocols.
    /// </summary>
    private static readonly Regex SafeKeyPattern =
        new(@"^[a-zA-Z0-9.\-+]+$", RegexOptions.Compiled);

    public void Sync(Dictionary<string, HandlerMappingEntry> effectiveHandlerMappings, List<AppEntry> apps)
    {
        var launcherPath = launcherPathOverride ?? Path.Combine(AppContext.BaseDirectory, Constants.LauncherExeName);
        if (!File.Exists(launcherPath))
        {
            log.Warn($"AppHandlerRegistrationService.Sync: launcher not found at {launcherPath}, skipping");
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
                log.Warn($"AppHandlerRegistrationService.Sync: skipping association key '{kvp.Key}' (contains unsafe characters)");
                continue;
            }

            var appEntry = apps.FirstOrDefault(a => a.Id == kvp.Value.AppId);
            if (appEntry == null)
            {
                log.Warn($"AppHandlerRegistrationService.Sync: skipping association '{kvp.Key}' → '{kvp.Value.AppId}' (app not found)");
                continue;
            }

            validMappings[kvp.Key] = kvp.Value.AppId;
        }

        log.Info($"AppHandlerRegistrationService.Sync: registering {validMappings.Count} association(s) in HKLM");

        if (validMappings.Count == 0)
        {
            // No valid mappings — remove everything to avoid stale registry entries
            UnregisterAll();
            return;
        }

        try
        {
            // Remove stale ProgIds (RunFence_* subkeys not in current valid mappings);
            // also removes legacy RunFence_Handler key if present from an older installation
            RemoveStaleProgIds(validMappings);

            // Ensure Capabilities key exists at the standard location (Software\RunFence\Capabilities)
            EnsureCapabilities();

            // Clear and rebuild URLAssociations and FileAssociations
            RebuildCapabilities(validMappings);

            // Create/update per-association ProgIds
            foreach (var (association, appId) in validMappings)
            {
                RegisterAssociationProgId(association, apps.First(a => a.Id == appId), launcherPath);
            }

            ShellNative.SHChangeNotify(ShellNative.SHCNE_ASSOCCHANGED, ShellNative.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            log.Info("AppHandlerRegistrationService.Sync: complete");
        }
        catch (Exception ex)
        {
            log.Error("AppHandlerRegistrationService.Sync: failed", ex);
        }
    }

    public void UnregisterAll()
    {
        log.Info("AppHandlerRegistrationService.UnregisterAll: removing all handler registrations from HKLM");

        try
        {
            // Delete all RunFence_* ProgIds under Software\Classes
            using var classesKey = _hklm.OpenSubKey(@"Software\Classes", writable: true);
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
                        log.Warn($"Failed to delete ProgId '{name}': {ex.Message}");
                    }
                }
            }

            // Remove Capabilities key (standard location)
            try
            {
                _hklm.DeleteSubKeyTree(Constants.HandlerCapabilitiesRegistryPath, throwOnMissingSubKey: false);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to delete Capabilities key: {ex.Message}");
            }

            // Remove RegisteredApplications entry
            using var regAppsKey = _hklm.OpenSubKey(@"Software\RegisteredApplications", writable: true);
            regAppsKey?.DeleteValue(Constants.HandlerRegisteredAppName, throwOnMissingValue: false);

            ShellNative.SHChangeNotify(ShellNative.SHCNE_ASSOCCHANGED, ShellNative.SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            log.Info("AppHandlerRegistrationService.UnregisterAll: complete");
        }
        catch (Exception ex)
        {
            log.Error("AppHandlerRegistrationService.UnregisterAll: failed", ex);
        }
    }

    private void RegisterAssociationProgId(string association, AppEntry app, string launcherPath)
    {
        var progId = Constants.HandlerProgIdPrefix + association;
        var classesPrefix = $@"Software\Classes\{progId}";

        var iconPath = ResolveIconPath(app, launcherPath);

        using (var rootKey = _hklm.CreateSubKey(classesPrefix))
            rootKey.SetValue(null, progId);

        using (var iconKey = _hklm.CreateSubKey($@"{classesPrefix}\DefaultIcon"))
            iconKey.SetValue(null, $"\"{iconPath}\",0");

        // Shell command uses unquoted %1 — Launcher extracts it via CommandLineHelper.SkipArgs
        using (var commandKey = _hklm.CreateSubKey($@"{classesPrefix}\shell\open\command"))
            commandKey.SetValue(null, $"\"{launcherPath}\" --resolve \"{association}\" %1");
    }

    private void EnsureCapabilities()
    {
        // Standard location outside Software\Classes — prevents Windows from double-discovering
        // RunFence via both RegisteredApplications and a Capabilities subkey on a ProgId.
        using (var capsKey = _hklm.CreateSubKey(Constants.HandlerCapabilitiesRegistryPath))
        {
            capsKey.SetValue("ApplicationName", Constants.HandlerRegisteredAppName);
            capsKey.SetValue("ApplicationDescription", "Launches files and URLs through RunFence-managed applications");
        }

        using var regAppsKey = _hklm.CreateSubKey(@"Software\RegisteredApplications");
        regAppsKey.SetValue(Constants.HandlerRegisteredAppName, Constants.HandlerCapabilitiesRegistryPath);
    }

    private void RebuildCapabilities(Dictionary<string, string> validMappings)
    {
        var capsPrefix = Constants.HandlerCapabilitiesRegistryPath;

        // Clear existing URLAssociations and FileAssociations subkeys before rebuilding
        try
        {
            _hklm.DeleteSubKeyTree($@"{capsPrefix}\URLAssociations", throwOnMissingSubKey: false);
        }
        catch
        {
        }

        try
        {
            _hklm.DeleteSubKeyTree($@"{capsPrefix}\FileAssociations", throwOnMissingSubKey: false);
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
            using var urlKey = _hklm.CreateSubKey($@"{capsPrefix}\URLAssociations");
            foreach (var kvp in urlAssociations)
                urlKey.SetValue(kvp.Key, kvp.Value);
        }

        if (fileAssociations.Count > 0)
        {
            using var fileKey = _hklm.CreateSubKey($@"{capsPrefix}\FileAssociations");
            foreach (var kvp in fileAssociations)
                fileKey.SetValue(kvp.Key, kvp.Value);
        }
    }

    private void RemoveStaleProgIds(Dictionary<string, string> validMappings)
    {
        // RunFence_Handler is no longer created; if present from an older installation it will be removed here.
        var desiredProgIds = new HashSet<string>(
            validMappings.Keys.Select(k => Constants.HandlerProgIdPrefix + k),
            StringComparer.OrdinalIgnoreCase);

        using var classesKey = _hklm.OpenSubKey(@"Software\Classes", writable: true);
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
                log.Warn($"Failed to remove stale ProgId '{name}': {ex.Message}");
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

    private Dictionary<string, HandlerMappingEntry> FilterForLicense(Dictionary<string, HandlerMappingEntry> mappings)
    {
        if (licenseService.IsLicensed)
            return mappings;

        // Evaluation mode: only browser associations allowed
        var filtered = new Dictionary<string, HandlerMappingEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in mappings)
        {
            if (Constants.BrowserAssociations.Contains(kvp.Key))
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
