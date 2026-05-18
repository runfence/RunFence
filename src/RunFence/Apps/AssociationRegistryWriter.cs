using Microsoft.Win32;
using RunFence.Core;
using RunFence.Core.Helpers;

namespace RunFence.Apps;

/// <summary>
/// Performs all HKCU registry write operations for per-user association overrides.
/// Receives the HKU root key as a method parameter — it is an OS singleton, not a DI dependency.
/// </summary>
public class AssociationRegistryWriter(
    ILoggingService log,
    Func<RegistryKey, AssociationFallbackRegistry> registryFactory,
    Func<IAssociationFallbackRegistry, AssociationFallbackRestoreService> restoreServiceFactory)
{
    /// <summary>
    /// Sets the HKCU default value for a file extension to the RunFence ProgId,
    /// storing the original value in RunFenceFallback so it can be restored later.
    /// No-ops when already set to the expected ProgId.
    /// </summary>
    public void AutoSetExtension(RegistryKey hku, string sid, string key)
    {
        using var extKey = hku.CreateSubKey($@"{sid}\Software\Classes\{key}");

        var currentDefault = extKey.GetValue(null) as string;
        var expectedProgId = PathConstants.HandlerProgIdPrefix + key;

        if (string.Equals(currentDefault, expectedProgId, StringComparison.OrdinalIgnoreCase))
            return;

        if (extKey.GetValue(PathConstants.RunFenceFallbackValueName) == null)
            extKey.SetValue(PathConstants.RunFenceFallbackValueName, currentDefault ?? string.Empty);

        extKey.SetValue(null, expectedProgId);
    }

    /// <summary>
    /// Sets the HKCU open command for a URL protocol to the RunFence launcher,
    /// storing the original command in RunFenceFallback so it can be restored later.
    /// No-ops when already set to the expected command.
    /// </summary>
    public void AutoSetProtocol(RegistryKey hku, string sid, string key, string launcherPath)
    {
        using var protocolKey = hku.CreateSubKey($@"{sid}\Software\Classes\{key}");
        using var commandKey = hku.CreateSubKey($@"{sid}\Software\Classes\{key}\shell\open\command");

        var currentCommand = commandKey.GetValue(null) as string;
        var expectedCommand = $"\"{launcherPath}\" --resolve \"{key}\" %1";

        if (string.Equals(currentCommand, expectedCommand, StringComparison.OrdinalIgnoreCase))
            return;

        if (protocolKey.GetValue(PathConstants.RunFenceFallbackValueName) == null)
            protocolKey.SetValue(PathConstants.RunFenceFallbackValueName, currentCommand ?? string.Empty);

        protocolKey.SetValue("URL Protocol", string.Empty);
        commandKey.SetValue(null, expectedCommand);
    }

    /// <summary>
    /// Sets the HKCU default value for a file extension to a direct class name,
    /// storing the original ProgId in RunFenceFallback.
    /// No-ops when already set to the expected class name.
    /// </summary>
    public void AutoSetDirectClassExtension(RegistryKey hku, string sid, string key, string className)
    {
        using var extKey = hku.CreateSubKey($@"{sid}\Software\Classes\{key}");
        var currentDefault = extKey.GetValue(null) as string;

        if (string.Equals(currentDefault, className, StringComparison.OrdinalIgnoreCase))
            return;

        if (extKey.GetValue(PathConstants.RunFenceFallbackValueName) == null)
            extKey.SetValue(PathConstants.RunFenceFallbackValueName, currentDefault ?? string.Empty);

        extKey.SetValue(null, className);
    }

    /// <summary>
    /// Sets the HKCU open command for a file extension to a direct command string,
    /// storing the original ProgId in RunFenceFallback.
    /// No-ops when already set to the expected command.
    /// </summary>
    public void AutoSetDirectCommandExtension(RegistryKey hku, string sid, string key, string command)
    {
        using var extKey = hku.CreateSubKey($@"{sid}\Software\Classes\{key}");
        using var commandKey = hku.CreateSubKey($@"{sid}\Software\Classes\{key}\shell\open\command");

        var currentCommand = commandKey.GetValue(null) as string;

        if (string.Equals(currentCommand, command, StringComparison.OrdinalIgnoreCase))
            return;

        // Store the default value (ProgId) in RunFenceFallback — same as class-based and protocol handlers
        if (extKey.GetValue(PathConstants.RunFenceFallbackValueName) == null)
        {
            var currentDefault = extKey.GetValue(null) as string;
            extKey.SetValue(PathConstants.RunFenceFallbackValueName, currentDefault ?? string.Empty);
        }

        commandKey.SetValue(null, command);
    }

    /// <summary>
    /// Sets the HKCU open command for a URL protocol to a direct command string,
    /// storing the original command in RunFenceFallback.
    /// No-ops when already set to the expected command.
    /// </summary>
    public void AutoSetDirectCommandProtocol(RegistryKey hku, string sid, string key, string command)
    {
        using var protocolKey = hku.CreateSubKey($@"{sid}\Software\Classes\{key}");
        using var commandKey = hku.CreateSubKey($@"{sid}\Software\Classes\{key}\shell\open\command");

        var currentCommand = commandKey.GetValue(null) as string;

        if (string.Equals(currentCommand, command, StringComparison.OrdinalIgnoreCase))
            return;

        if (protocolKey.GetValue(PathConstants.RunFenceFallbackValueName) == null)
            protocolKey.SetValue(PathConstants.RunFenceFallbackValueName, currentCommand ?? string.Empty);

        protocolKey.SetValue("URL Protocol", string.Empty);
        commandKey.SetValue(null, command);
    }

    /// <summary>
    /// Restores the original handler for the given association key by reading the RunFenceFallback value.
    /// </summary>
    public void RestoreKey(
        RegistryKey hku,
        string sid,
        string key)
    {
        var registry = registryFactory(hku);
        var restoreService = restoreServiceFactory(registry);
        restoreService.RestoreFromFallback(
            key,
            FallbackCleanupMode.RemoveRunFenceOverrideThenRestoreFallback,
            sid);
    }

    /// <summary>
    /// Removes stale per-user RunFence ProgId entries (backward compatibility migration
    /// from per-user ProgIds to HKLM ProgIds).
    /// </summary>
    public void CleanStalePerUserProgIds(RegistryKey classesKey, string contextDescription)
    {
        foreach (var name in classesKey.GetSubKeyNames()
                     .Where(n => n.StartsWith(PathConstants.HandlerProgIdPrefix, StringComparison.OrdinalIgnoreCase))
                     .ToList())
        {
            try { classesKey.DeleteSubKeyTree(name, throwOnMissingSubKey: false); }
            catch (Exception ex) { log.Warn($"AssociationRegistryWriter: failed to delete stale ProgId '{name}' for {contextDescription}: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Returns true for HKU subkey names that correspond to user SIDs (S-1-5-21-*),
    /// excluding _Classes suffix variants.
    /// </summary>
    public static bool IsTargetSidSubKey(string name)
    {
        return name.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase)
               && !name.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase);
    }
}
