using Microsoft.Win32;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Apps;

/// <summary>
/// Scans the interactive user's HKCU to find association overrides for import as direct handler mappings.
/// Cache is per-instance (one instance per dialog).
/// </summary>
public class InteractiveUserAssociationReader : IInteractiveUserAssociationReader
{
    private readonly IUserHiveManager _hiveManager;
    private readonly IInteractiveUserResolver _interactiveUserResolver;
    private readonly RegistryKey _hku;
    private readonly RegistryKey _hklm;
    private IReadOnlyList<InteractiveAssociationEntry>? _cache;

    public InteractiveUserAssociationReader(
        IUserHiveManager hiveManager,
        IInteractiveUserResolver interactiveUserResolver,
        RegistryKey? hkuOverride = null,
        RegistryKey? hklmOverride = null)
    {
        _hiveManager = hiveManager;
        _interactiveUserResolver = interactiveUserResolver;
        _hku = hkuOverride ?? Registry.Users;
        _hklm = hklmOverride ?? Registry.LocalMachine;
    }

    public IReadOnlyList<InteractiveAssociationEntry> GetInteractiveUserAssociations()
    {
        if (_cache != null)
            return _cache;

        var sid = _interactiveUserResolver.GetInteractiveUserSid();
        if (string.IsNullOrEmpty(sid))
        {
            _cache = [];
            return _cache;
        }

        using var hiveHandle = _hiveManager.EnsureHiveLoaded(sid);

        var results = new Dictionary<string, InteractiveAssociationEntry>(StringComparer.OrdinalIgnoreCase);

        using var hkuClasses = _hku.OpenSubKey($@"{sid}\Software\Classes");
        using var hklmClasses = _hklm.OpenSubKey(@"Software\Classes");

        // Scan Software\Classes for extensions and protocols
        if (hkuClasses != null)
        {
            foreach (var keyName in hkuClasses.GetSubKeyNames())
            {
                if (keyName.StartsWith('.'))
                {
                    var entry = ResolveExtension(keyName, sid, hkuClasses, hklmClasses);
                    if (entry.HasValue)
                        results[keyName] = BuildEntry(keyName, entry.Value);
                }
                else
                {
                    using var protocolSubKey = hkuClasses.OpenSubKey(keyName);
                    if (protocolSubKey?.GetValue("URL Protocol") == null)
                        continue;

                    using var cmdKey = protocolSubKey.OpenSubKey(@"shell\open\command");
                    var command = cmdKey?.GetValue(null) as string;
                    if (string.IsNullOrEmpty(command))
                        continue;

                    // Skip RunFence-managed protocols
                    if (IsRunFenceCommand(command))
                        continue;

                    results[keyName] = BuildEntry(keyName, new DirectHandlerEntry { Command = command });
                }
            }
        }

        // Also scan UserChoice for extensions not found via Software\Classes
        using var fileExtsKey = _hku.OpenSubKey($@"{sid}\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts");
        if (fileExtsKey != null)
        {
            foreach (var extName in fileExtsKey.GetSubKeyNames())
            {
                if (!extName.StartsWith('.') || results.ContainsKey(extName))
                    continue;

                using var userChoiceKey = fileExtsKey.OpenSubKey($@"{extName}\UserChoice");
                var progId = userChoiceKey?.GetValue("ProgId") as string;
                if (string.IsNullOrEmpty(progId) || AssociationCommandHelper.IsRunFenceProgId(progId))
                    continue;

                var entry = ResolveProgId(progId, hkuClasses, hklmClasses);
                if (entry.HasValue)
                    results[extName] = BuildEntry(extName, entry.Value);
            }
        }

        _cache = results.Values.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToList();
        return _cache;
    }

    public DirectHandlerEntry? GetAssociationHandler(string key)
    {
        var sid = _interactiveUserResolver.GetInteractiveUserSid();
        if (string.IsNullOrEmpty(sid))
            return null;

        using var hiveHandle = _hiveManager.EnsureHiveLoaded(sid);

        using var hkuClasses = _hku.OpenSubKey($@"{sid}\Software\Classes");
        using var hklmClasses = _hklm.OpenSubKey(@"Software\Classes");

        if (key.StartsWith('.'))
        {
            var entry = ResolveExtension(key, sid, hkuClasses, hklmClasses);
            return entry;
        }
        else
        {
            using var protocolKey = hkuClasses?.OpenSubKey(key);
            if (protocolKey?.GetValue("URL Protocol") == null)
                return null;

            using var cmdKey = protocolKey.OpenSubKey(@"shell\open\command");
            var command = cmdKey?.GetValue(null) as string;
            if (string.IsNullOrEmpty(command) || IsRunFenceCommand(command))
                return null;

            return new DirectHandlerEntry { Command = command };
        }
    }

    private DirectHandlerEntry? ResolveExtension(string key, string sid,
        RegistryKey? hkuClasses, RegistryKey? hklmClasses)
    {
        // Step 1: direct HKCU default value
        string? progId = null;
        if (hkuClasses != null)
        {
            using var extKey = hkuClasses.OpenSubKey(key);
            progId = extKey?.GetValue(null) as string;
        }

        // Step 2: UserChoice ProgId if no direct HKCU default
        if (string.IsNullOrEmpty(progId))
        {
            using var userChoiceKey = _hku.OpenSubKey(
                $@"{sid}\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{key}\UserChoice");
            progId = userChoiceKey?.GetValue("ProgId") as string;
        }

        if (string.IsNullOrEmpty(progId) || AssociationCommandHelper.IsRunFenceProgId(progId))
            return null;

        return ResolveProgId(progId, hkuClasses, hklmClasses);
    }

    private DirectHandlerEntry? ResolveProgId(string progId,
        RegistryKey? hkuClasses, RegistryKey? hklmClasses)
    {
        // Check if progId exists as class in HKLM (stable, machine-wide)
        if (hklmClasses != null)
        {
            using var hklmProgIdKey = hklmClasses.OpenSubKey($@"{progId}\shell\open\command");
            if (hklmProgIdKey != null)
                return new DirectHandlerEntry { ClassName = progId };
        }

        // Try to resolve command from HKCU
        if (hkuClasses != null)
        {
            using var hkuProgIdKey = hkuClasses.OpenSubKey($@"{progId}\shell\open\command");
            var command = hkuProgIdKey?.GetValue(null) as string;
            if (!string.IsNullOrEmpty(command) && !IsRunFenceCommand(command))
                return new DirectHandlerEntry { Command = command };
        }

        return null;
    }

    private bool IsRunFenceCommand(string command)
    {
        var exePath = AssociationRegistryCommandParser.ExtractExeFromCommand(command);
        if (string.IsNullOrEmpty(exePath))
            return false;

        var exeName = Path.GetFileName(exePath);
        return string.Equals(exeName, Constants.LauncherExeName, StringComparison.OrdinalIgnoreCase);
    }

    private static InteractiveAssociationEntry BuildEntry(string key, DirectHandlerEntry handler)
    {
        string description;
        if (handler.Command != null)
        {
            var exePath = AssociationRegistryCommandParser.ExtractExeFromCommand(handler.Command);
            var exeName = !string.IsNullOrEmpty(exePath)
                ? Path.GetFileNameWithoutExtension(exePath)
                : handler.Command;
            description = exeName ?? handler.Command;
        }
        else
        {
            description = handler.ClassName ?? string.Empty;
        }

        return new InteractiveAssociationEntry(key, handler, description);
    }
}
