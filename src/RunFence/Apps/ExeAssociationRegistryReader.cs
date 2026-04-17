using Microsoft.Win32;
using RunFence.Acl.Permissions;
using RunFence.Core.Helpers;
using RunFence.Infrastructure;

namespace RunFence.Apps;

/// <summary>
/// Scans the interactive user's HKU and HKLM\Software\Classes to find which associations
/// an exe handles and what arguments each uses. Results are cached per exe within the
/// instance's lifetime (one instance per dialog).
/// </summary>
public class ExeAssociationRegistryReader : IExeAssociationRegistryReader
{
    private readonly IUserHiveManager _hiveManager;
    private readonly IInteractiveUserResolver _interactiveUserResolver;
    private readonly RegistryKey _hklm;
    private readonly RegistryKey _hku;

    private readonly Dictionary<string, IReadOnlyList<string>> _handleAssocCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _argsCache =
        new(StringComparer.OrdinalIgnoreCase);

    public ExeAssociationRegistryReader(
        IUserHiveManager hiveManager,
        IInteractiveUserResolver interactiveUserResolver,
        RegistryKey? hklmOverride = null,
        RegistryKey? hkuOverride = null)
    {
        _hiveManager = hiveManager;
        _interactiveUserResolver = interactiveUserResolver;
        _hklm = hklmOverride ?? Registry.LocalMachine;
        _hku = hkuOverride ?? Registry.Users;
    }

    private string? GetInteractiveSid() => _interactiveUserResolver.GetInteractiveUserSid();

    private static bool IsAppDataPath(string exePath) =>
        exePath.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeCacheKey(string exePath, string key) =>
        exePath.ToUpperInvariant() + "\0" + key.ToLowerInvariant();

    /// <remarks>
    /// Iterates all HKU\{sid}\Software\Classes subkeys, then supplements with HKLM common suggestions.
    /// Results cached per exe within the instance lifetime (one instance per dialog) — acceptable performance.
    /// </remarks>
    public IReadOnlyList<string> GetHandledAssociations(string exePath)
    {
        if (_handleAssocCache.TryGetValue(exePath, out var cached))
            return cached;

        var interactiveSid = GetInteractiveSid();
        if (interactiveSid == null)
        {
            _handleAssocCache[exePath] = [];
            return [];
        }

        using var hiveHandle = _hiveManager.EnsureHiveLoaded(interactiveSid);

        var foundKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (var hkuClasses = _hku.OpenSubKey($@"{interactiveSid}\Software\Classes"))
        using (var hklmClasses = _hklm.OpenSubKey(@"Software\Classes"))
        {
            if (hkuClasses != null)
            {
                foreach (var keyName in hkuClasses.GetSubKeyNames())
                {
                    var command = ResolveCommandForKey(keyName, hkuClasses, hklmClasses);
                    if (TryMatchAndCache(exePath, keyName, command))
                        foundKeys.Add(keyName);
                }
            }

            if (!IsAppDataPath(exePath))
            {
                foreach (var keyName in AppHandlerRegistrationService.CommonAssociationSuggestions)
                {
                    if (foundKeys.Contains(keyName))
                        continue;

                    var command = ResolveCommandForKey(keyName, null, hklmClasses);
                    if (TryMatchAndCache(exePath, keyName, command))
                        foundKeys.Add(keyName);
                }
            }
        }

        var result = foundKeys.ToList();
        _handleAssocCache[exePath] = result;
        return result;
    }

    public string? GetNonDefaultArguments(string exePath, string key)
    {
        var cacheKey = NormalizeCacheKey(exePath, key);
        if (_argsCache.TryGetValue(cacheKey, out var cached))
            return cached;

        // Full scan already done but this key wasn't matched — must be absent.
        if (_handleAssocCache.ContainsKey(exePath))
        {
            _argsCache[cacheKey] = null;
            return null;
        }

        var interactiveSid = GetInteractiveSid();
        if (interactiveSid == null)
        {
            _argsCache[cacheKey] = null;
            return null;
        }

        using var hiveHandle = _hiveManager.EnsureHiveLoaded(interactiveSid);

        using (var hkuClasses = _hku.OpenSubKey($@"{interactiveSid}\Software\Classes"))
        using (var hklmClasses = _hklm.OpenSubKey(@"Software\Classes"))
        {
            // Try HKU first (with HKLM as ProgId fallback for extension resolution), then HKLM.
            var hkuCommand = ResolveCommandForKey(key, hkuClasses, hklmClasses);
            if (hkuCommand != null && TryMatchAndCache(exePath, key, hkuCommand))
                return _argsCache[cacheKey];

            // Try HKLM unless the exe is in AppData
            if (!IsAppDataPath(exePath))
            {
                var hklmCommand = ResolveCommandForKey(key, null, hklmClasses);
                if (hklmCommand != null && TryMatchAndCache(exePath, key, hklmCommand))
                    return _argsCache[cacheKey];
            }
        }

        _argsCache[cacheKey] = null;
        return null;
    }

    /// <summary>
    /// Resolves the open command for a key (extension or protocol) from the given classes hive.
    /// For extensions, uses <see cref="ResolveExtensionCommand"/> with an optional HKLM fallback
    /// for ProgId resolution. For protocols, looks up <c>shell\open\command</c> directly.
    /// Pass <paramref name="hkuClasses"/> as null and <paramref name="hklmClasses"/> non-null
    /// to look up from HKLM only.
    /// </summary>
    private string? ResolveCommandForKey(string key, RegistryKey? hkuClasses, RegistryKey? hklmClasses)
    {
        if (key.StartsWith('.'))
        {
            // For extensions in HKU, pass hklmClasses as fallback so ProgId shell commands
            // defined only in HKLM can still be resolved.
            var classesBase = hkuClasses ?? hklmClasses;
            var fallback = hkuClasses != null ? hklmClasses : null;
            return ResolveExtensionCommand(key, classesBase, fallback);
        }

        // Protocol: read shell\open\command from whichever hive is provided
        if (hkuClasses == null && hklmClasses == null)
            return null;

        if (hkuClasses != null)
        {
            using var protocolSubKey = hkuClasses.OpenSubKey(key);
            if (protocolSubKey?.GetValue("URL Protocol") != null)
            {
                using var cmdKey = protocolSubKey.OpenSubKey(@"shell\open\command");
                return cmdKey?.GetValue(null) as string;
            }

            return null;
        }

        // HKLM-only protocol lookup (no URL Protocol marker check for HKLM suggestions).
        // hklmClasses is non-null here: both-null case exits at line 159, hkuClasses-only exits at 168/171.
        using var hklmCmdKey = hklmClasses!.OpenSubKey(key + @"\shell\open\command");
        return hklmCmdKey?.GetValue(null) as string;
    }

    private string? ResolveExtensionCommand(string extKeyName, RegistryKey? classesBase, RegistryKey? hklmClassesFallback)
    {
        if (classesBase == null)
            return null;

        using var extSubKey = classesBase.OpenSubKey(extKeyName);
        if (extSubKey == null)
            return null;

        var progId = extSubKey.GetValue(null) as string;
        if (string.IsNullOrEmpty(progId) || AssociationCommandHelper.IsRunFenceProgId(progId))
            return null;

        using var cmdKey = classesBase.OpenSubKey(progId + @"\shell\open\command");
        if (cmdKey != null)
        {
            var cmd = cmdKey.GetValue(null) as string;
            if (cmd != null)
                return cmd;
        }

        if (hklmClassesFallback != null)
        {
            using var hklmCmdKey = hklmClassesFallback.OpenSubKey(progId + @"\shell\open\command");
            return hklmCmdKey?.GetValue(null) as string;
        }

        return null;
    }

    public bool IsRegisteredProgId(string extensionKey, string className)
    {
        if (!extensionKey.StartsWith('.'))
            return false;

        using var hklmCmd = _hklm.OpenSubKey($@"Software\Classes\{className}\shell\open\command");
        return hklmCmd != null;
    }

    private bool TryMatchAndCache(string exePath, string key, string? command)
    {
        if (command == null)
            return false;

        var cmdExe = AssociationRegistryCommandParser.ExtractExeFromCommand(command);
        var expandedCmd = Environment.ExpandEnvironmentVariables(cmdExe ?? "");
        var expandedExe = Environment.ExpandEnvironmentVariables(exePath);

        if (!string.Equals(expandedCmd, expandedExe, StringComparison.OrdinalIgnoreCase))
            return false;

        _argsCache[NormalizeCacheKey(exePath, key)] =
            AssociationRegistryCommandParser.ExtractNonDefaultArgs(command);
        return true;
    }
}
