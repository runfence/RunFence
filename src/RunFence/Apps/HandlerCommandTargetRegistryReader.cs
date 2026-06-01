using RunFence.Core;
using RunFence.Core.Helpers;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.Apps;

public sealed class HandlerCommandTargetRegistryReader(
    IInteractiveUserSidResolver interactiveUserSidResolver,
    ISidResolver sidResolver,
    IHkuRootProvider hkuRootProvider,
    IHklmClassesRootProvider hklmClassesRootProvider,
    IAssociationRegistryProtocolMarkerReader protocolMarkerReader) : IHandlerCommandTargetReader
{
    private const string UserClassesPath = @"Software\Classes";
    private const string DefaultOpenCommandPath = @"shell\open\command";
    private const string DefaultIconSubKey = @"DefaultIcon";

    public IReadOnlyList<HandlerCommandTarget> ReadTargets(string? targetAccountSid)
    {
        var results = new List<HandlerCommandTarget>();
        var emittedAssociationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var usersRoot = hkuRootProvider.OpenUsersRoot();
        using var hklmClasses = hklmClassesRootProvider.OpenClassesRoot();
        var interactiveSid = interactiveUserSidResolver.GetInteractiveUserSid();
        var currentSid = sidResolver.GetCurrentUserSid();
        using var targetAccountClasses = TryOpenUserClasses(usersRoot, targetAccountSid);
        using var interactiveUserClasses = TryOpenUserClasses(usersRoot, interactiveSid);
        using var currentUserClasses = TryOpenUserClasses(usersRoot, currentSid);

        AddScopeTargets(targetAccountClasses, HandlerCommandTargetRegistryScope.TargetAccount, hklmClasses, results, emittedAssociationKeys);
        AddScopeTargets(interactiveUserClasses, HandlerCommandTargetRegistryScope.InteractiveUser, hklmClasses, results, emittedAssociationKeys);
        AddScopeTargets(currentUserClasses, HandlerCommandTargetRegistryScope.CurrentUser, hklmClasses, results, emittedAssociationKeys);
        AddScopeTargets(hklmClasses, HandlerCommandTargetRegistryScope.Hklm, null, results, emittedAssociationKeys);

        return results;
    }

    private void AddScopeTargets(
        IRegistryKey? classesRoot,
        HandlerCommandTargetRegistryScope scope,
        IRegistryKey? hklmClasses,
        List<HandlerCommandTarget> output,
        HashSet<string> emittedAssociationKeys)
    {
        if (classesRoot == null)
            return;

        string[] subKeyNames;
        try
        {
            subKeyNames = classesRoot.GetSubKeyNames();
        }
        catch
        {
            return;
        }

        foreach (var keyName in subKeyNames)
        {
            if (string.IsNullOrWhiteSpace(keyName))
                continue;

            if (emittedAssociationKeys.Contains(keyName))
                continue;

            using var associationKey = classesRoot.OpenSubKey(keyName);
            if (associationKey == null)
                continue;

            if (keyName.StartsWith(".", StringComparison.Ordinal))
            {
                AddExtensionTarget(
                    associationKey,
                    keyName,
                    classesRoot,
                    hklmClasses,
                    scope,
                    output,
                    emittedAssociationKeys);
                continue;
            }

            if (protocolMarkerReader.HasUrlProtocolMarker(associationKey))
            {
                var command = ReadOpenCommand(associationKey.OpenSubKey(DefaultOpenCommandPath));
                if (AddCommandTarget(
                    keyName,
                    command,
                    scope,
                    output,
                    ReadPreferredDefaultIconValue(classesRoot, keyName, hklmClasses)))
                {
                    emittedAssociationKeys.Add(keyName);
                }
            }
        }
    }

    private void AddExtensionTarget(
        IRegistryKey extensionClassKey,
        string extensionKeyName,
        IRegistryKey classesRoot,
        IRegistryKey? hklmClasses,
        HandlerCommandTargetRegistryScope scope,
        List<HandlerCommandTarget> output,
        HashSet<string> emittedAssociationKeys)
    {
        var className = extensionClassKey.GetValue(null) as string;
        if (string.IsNullOrWhiteSpace(className) || AssociationCommandHelper.IsRunFenceProgId(className))
            return;

        var command = ReadOpenCommand(classesRoot.OpenSubKey($@"{className}\{DefaultOpenCommandPath}"))
            ?? ReadOpenCommand(hklmClasses?.OpenSubKey($@"{className}\{DefaultOpenCommandPath}"));
        if (command == null || AssociationCommandHelper.IsRunFenceLauncherCommand(command))
            return;

        if (AddCommandTarget(
            extensionKeyName,
            command,
            scope,
            output,
            ReadPreferredDefaultIconValue(classesRoot, className, hklmClasses)))
        {
            emittedAssociationKeys.Add(extensionKeyName);
        }
    }

    private bool AddCommandTarget(
        string associationKey,
        string? command,
        HandlerCommandTargetRegistryScope scope,
        List<HandlerCommandTarget> output,
        string? defaultIconRawValue = null)
    {
        if (string.IsNullOrWhiteSpace(command))
            return false;

        var rawPath = AssociationRegistryCommandParser.ExtractExeFromCommand(command);
        if (string.IsNullOrWhiteSpace(rawPath))
            return false;

        output.Add(new HandlerCommandTarget(
            Environment.ExpandEnvironmentVariables(rawPath),
            scope,
            associationKey,
            command,
            defaultIconRawValue,
            ResolveDefaultIconPath(defaultIconRawValue))
        {
            HasExplicitDefaultIcon = !string.IsNullOrWhiteSpace(defaultIconRawValue)
        });
        return true;
    }

    private static string? ResolveDefaultIconPath(string? defaultIconRawValue)
    {
        if (string.IsNullOrWhiteSpace(defaultIconRawValue))
            return null;

        var defaultIconValue = defaultIconRawValue.Trim();
        string iconPath;
        if (defaultIconValue.StartsWith("\"", StringComparison.Ordinal))
        {
            var closingQuoteIndex = defaultIconValue.IndexOf('"', startIndex: 1);
            if (closingQuoteIndex <= 0)
                return null;

            iconPath = defaultIconValue.Substring(1, closingQuoteIndex - 1);
        }
        else
        {
            var commaIndex = defaultIconValue.IndexOf(',');
            iconPath = commaIndex < 0
                ? defaultIconValue
                : defaultIconValue[..commaIndex];
        }

        iconPath = iconPath.Trim();
        if (string.IsNullOrWhiteSpace(iconPath))
            return null;

        var expandedPath = Environment.ExpandEnvironmentVariables(iconPath);
        return string.IsNullOrWhiteSpace(expandedPath) ? null : expandedPath;
    }

    private static string? ReadPreferredDefaultIconValue(
        IRegistryKey? primaryClasses,
        string className,
        IRegistryKey? fallbackClasses = null)
    {
        var primaryValue = ReadDefaultIconValue(primaryClasses, className);
        if (!string.IsNullOrWhiteSpace(primaryValue))
            return primaryValue;

        var fallbackValue = ReadDefaultIconValue(fallbackClasses, className);
        if (!string.IsNullOrWhiteSpace(fallbackValue))
            return fallbackValue;

        return primaryValue ?? fallbackValue;
    }

    private static string? ReadDefaultIconValue(IRegistryKey? baseClasses, string className)
    {
        using var iconKey = baseClasses?.OpenSubKey($@"{className}\{DefaultIconSubKey}");
        return iconKey?.GetValue(null) as string;
    }

    private static string? ReadOpenCommand(IRegistryKey? openCommandKey)
    {
        using (openCommandKey)
            return openCommandKey?.GetValue(null) as string;
    }

    private static IRegistryKey? TryOpenUserClasses(IRegistryKey usersRoot, string? sid)
    {
        if (string.IsNullOrWhiteSpace(sid))
            return null;

        try
        {
            return usersRoot.OpenSubKey($@"{sid}\{UserClassesPath}");
        }
        catch
        {
            return null;
        }
    }
}
