using Microsoft.Win32;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Infrastructure;

namespace RunFence.Apps;

/// <summary>
/// Scans the selected app account's loaded HKU hive, the interactive user's HKU, and
/// HKLM\Software\Classes to find which associations an exe handles and what arguments
/// each uses. Results are cached within the instance's lifetime (one instance per dialog).
/// </summary>
public class ExeAssociationRegistryReader : IExeAssociationRegistryReader
{
    private readonly IUserHiveManager _hiveManager;
    private readonly IInteractiveUserResolver _interactiveUserResolver;
    private readonly IRegistryKey _hklm;
    private readonly IRegistryKey _hku;

    private readonly Dictionary<string, IReadOnlyList<string>> _handleAssocCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string?> _argsCache =
        new(StringComparer.OrdinalIgnoreCase);

    public ExeAssociationRegistryReader(
        IUserHiveManager hiveManager,
        IInteractiveUserResolver interactiveUserResolver,
        IAssociationRegistryProtocolMarkerReader protocolMarkerReader,
        IRegistryKey? hklmOverride = null,
        IRegistryKey? hkuOverride = null)
    {
        _hiveManager = hiveManager;
        _interactiveUserResolver = interactiveUserResolver;
        _protocolMarkerReader = protocolMarkerReader;
        _hklm = hklmOverride ?? new WindowsRegistryKey(Registry.LocalMachine);
        _hku = hkuOverride ?? new WindowsRegistryKey(Registry.Users);
    }

    private readonly IAssociationRegistryProtocolMarkerReader _protocolMarkerReader;

    private string? GetInteractiveSid() => _interactiveUserResolver.GetInteractiveUserSid();

    private static bool IsAppDataPath(string exePath) =>
        exePath.Contains(@"\AppData\", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeAssociationCacheKey(string exePath, string? accountSid, bool useLoadedAccountClasses) =>
        exePath.ToUpperInvariant() + "\0" + (accountSid?.ToUpperInvariant() ?? "") + "\0" + (useLoadedAccountClasses ? "1" : "0");

    private static string NormalizeArgumentsCacheKey(string exePath, string key, string? accountSid, bool useLoadedAccountClasses) =>
        NormalizeAssociationCacheKey(exePath, accountSid, useLoadedAccountClasses) + "\0" + key.ToLowerInvariant();

    private bool ShouldUseLoadedAccountClasses(string? accountSid, string? interactiveSid)
    {
        if (string.IsNullOrEmpty(accountSid)
            || string.Equals(accountSid, interactiveSid, StringComparison.OrdinalIgnoreCase)
            || !_hiveManager.IsHiveLoaded(accountSid))
        {
            return false;
        }

        return true;
    }

    private IRegistryKey? OpenLoadedAccountClasses(string? accountSid, bool useLoadedAccountClasses)
    {
        if (!useLoadedAccountClasses)
            return null;

        return _hku.OpenSubKey($@"{accountSid}\Software\Classes");
    }

    /// <remarks>
    /// Iterates the selected app account's loaded HKU\{sid}\Software\Classes subkeys, then
    /// the interactive user's HKU\{sid}\Software\Classes subkeys, then supplements with HKLM
    /// common suggestions. Results are cached per exe/account pair and loaded-hive state within
    /// the instance lifetime (one instance per dialog).
    /// </remarks>
    public IReadOnlyList<string> GetHandledAssociations(string exePath, string? accountSid = null)
    {
        var interactiveSid = GetInteractiveSid();
        var useLoadedAccountClasses = ShouldUseLoadedAccountClasses(accountSid, interactiveSid);
        var associationCacheKey = NormalizeAssociationCacheKey(exePath, accountSid, useLoadedAccountClasses);
        if (_handleAssocCache.TryGetValue(associationCacheKey, out var cached))
            return cached;

        using var hiveHandle = interactiveSid != null
            ? _hiveManager.EnsureHiveLoaded(interactiveSid)
            : null;

        var foundKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var accountClasses = OpenLoadedAccountClasses(accountSid, useLoadedAccountClasses);
        using var interactiveClasses = interactiveSid != null
            ? _hku.OpenSubKey($@"{interactiveSid}\Software\Classes")
            : null;
        using var hklmClasses = _hklm.OpenSubKey(@"Software\Classes");

        if (accountClasses != null)
        {
            foreach (var keyName in accountClasses.GetSubKeyNames())
            {
                if (foundKeys.Contains(keyName))
                    continue;

                var command = ResolveCommandForKey(keyName, accountClasses, hklmClasses);
                if (TryMatchAndCache(exePath, keyName, command, accountSid, useLoadedAccountClasses))
                    foundKeys.Add(keyName);
            }
        }

        if (interactiveClasses != null)
        {
            foreach (var keyName in interactiveClasses.GetSubKeyNames())
            {
                if (foundKeys.Contains(keyName))
                    continue;

                var command = ResolveCommandForKey(keyName, interactiveClasses, hklmClasses);
                if (TryMatchAndCache(exePath, keyName, command, accountSid, useLoadedAccountClasses))
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
                if (TryMatchAndCache(exePath, keyName, command, accountSid, useLoadedAccountClasses))
                    foundKeys.Add(keyName);
            }
        }

        var result = foundKeys.ToList();
        _handleAssocCache[associationCacheKey] = result;
        return result;
    }

    public string? GetNonDefaultArguments(string exePath, string key, string? accountSid = null)
    {
        var interactiveSid = GetInteractiveSid();
        var useLoadedAccountClasses = ShouldUseLoadedAccountClasses(accountSid, interactiveSid);
        var cacheKey = NormalizeArgumentsCacheKey(exePath, key, accountSid, useLoadedAccountClasses);
        if (_argsCache.TryGetValue(cacheKey, out var cached))
            return cached;

        if (_handleAssocCache.ContainsKey(NormalizeAssociationCacheKey(exePath, accountSid, useLoadedAccountClasses)))
        {
            _argsCache[cacheKey] = null;
            return null;
        }

        using var hiveHandle = interactiveSid != null
            ? _hiveManager.EnsureHiveLoaded(interactiveSid)
            : null;

        using var accountClasses = OpenLoadedAccountClasses(accountSid, useLoadedAccountClasses);
        using var interactiveClasses = interactiveSid != null
            ? _hku.OpenSubKey($@"{interactiveSid}\Software\Classes")
            : null;
        using var hklmClasses = _hklm.OpenSubKey(@"Software\Classes");

        if (accountClasses != null)
        {
            var accountCommand = ResolveCommandForKey(key, accountClasses, hklmClasses);
            if (accountCommand != null && TryMatchAndCache(exePath, key, accountCommand, accountSid, useLoadedAccountClasses))
                return _argsCache[cacheKey];
        }

        if (interactiveClasses != null)
        {
            var hkuCommand = ResolveCommandForKey(key, interactiveClasses, hklmClasses);
            if (hkuCommand != null && TryMatchAndCache(exePath, key, hkuCommand, accountSid, useLoadedAccountClasses))
                return _argsCache[cacheKey];
        }

        if (!IsAppDataPath(exePath))
        {
            var hklmCommand = ResolveCommandForKey(key, null, hklmClasses);
            if (hklmCommand != null && TryMatchAndCache(exePath, key, hklmCommand, accountSid, useLoadedAccountClasses))
                return _argsCache[cacheKey];
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
    private string? ResolveCommandForKey(string key, IRegistryKey? hkuClasses, IRegistryKey? hklmClasses)
    {
        if (key.StartsWith('.'))
        {
            var classesBase = hkuClasses ?? hklmClasses;
            var fallback = hkuClasses != null ? hklmClasses : null;
            return ResolveExtensionCommand(key, classesBase, fallback);
        }

        if (hkuClasses == null && hklmClasses == null)
            return null;

        if (hkuClasses != null)
        {
            using var protocolSubKey = hkuClasses.OpenSubKey(key);
            if (_protocolMarkerReader.HasUrlProtocolMarker(protocolSubKey))
            {
                using var cmdKey = protocolSubKey!.OpenSubKey(@"shell\open\command");
                return cmdKey?.GetValue(null) as string;
            }

            return null;
        }

        using var hklmCmdKey = hklmClasses!.OpenSubKey(key + @"\shell\open\command");
        return hklmCmdKey?.GetValue(null) as string;
    }

    private string? ResolveExtensionCommand(string extKeyName, IRegistryKey? classesBase, IRegistryKey? hklmClassesFallback)
    {
        using var extSubKey = classesBase?.OpenSubKey(extKeyName);
        if (extSubKey == null)
            return null;

        var progId = extSubKey.GetValue(null) as string;
        if (string.IsNullOrEmpty(progId) || AssociationCommandHelper.IsRunFenceProgId(progId))
            return null;

        using var cmdKey = classesBase!.OpenSubKey(progId + @"\shell\open\command");
        if (cmdKey?.GetValue(null) is string cmd)
            return cmd;

        using var hklmCmdKey = hklmClassesFallback?.OpenSubKey(progId + @"\shell\open\command");
        return hklmCmdKey?.GetValue(null) as string;
    }

    public bool IsRegisteredProgId(string extensionKey, string className)
    {
        if (!extensionKey.StartsWith('.'))
            return false;

        using var hklmCmd = _hklm.OpenSubKey($@"Software\Classes\{className}\shell\open\command");
        return hklmCmd != null;
    }

    private static bool CommandMatchesExe(string exePath, string? command)
    {
        if (command == null)
            return false;

        var cmdExe = AssociationRegistryCommandParser.ExtractExeFromCommand(command);
        if (cmdExe == null)
            return false;

        var expandedCmd = Environment.ExpandEnvironmentVariables(cmdExe);
        var expandedExe = Environment.ExpandEnvironmentVariables(exePath);
        return PathHelper.IsSamePath(expandedCmd, expandedExe);
    }

    private bool TryMatchAndCache(
        string exePath,
        string key,
        string? command,
        string? accountSid,
        bool useLoadedAccountClasses)
    {
        if (!CommandMatchesExe(exePath, command))
            return false;

        _argsCache[NormalizeArgumentsCacheKey(exePath, key, accountSid, useLoadedAccountClasses)] =
            AssociationRegistryCommandParser.ExtractNonDefaultArgs(command!);
        return true;
    }
}
