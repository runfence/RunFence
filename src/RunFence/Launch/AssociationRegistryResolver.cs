using RunFence.Core;
using RunFence.Core.Helpers;

namespace RunFence.Launch;

public class AssociationRegistryResolver(
    ILoggingService log,
    IHklmClassesRootProvider hklmClassesRootProvider,
    IHkuRootProvider hkuRootProvider,
    IAssociationRegistryProtocolMarkerReader protocolMarkerReader)
    : IAssociationRegistryReader
{
    public IEnumerable<AssociationRegistryCommandCandidate> ResolveFileCandidates(
        string sid,
        ProcessLaunchTarget originalTarget,
        bool rejectUserProfileHandlers,
        string? extension = null)
    {
        var effectiveExtension = string.IsNullOrEmpty(extension)
            ? Path.GetExtension(originalTarget.ExePath)
            : extension;
        if (string.IsNullOrEmpty(effectiveExtension))
            yield break;

        using var usersRoot = hkuRootProvider.OpenUsersRoot();
        using var hkuClasses = usersRoot.OpenSubKey($@"{sid}\Software\Classes");
        using var hklmClasses = hklmClassesRootProvider.OpenClassesRoot();

        var scopeLabel = BuildScopeLabel(sid, rejectUserProfileHandlers);
        var rawArgument = originalTarget.ExePath;
        var workingDirectory = ResolveAssociationWorkingDirectory(originalTarget);

        var userChoiceProgId = ReadStringValue(usersRoot.OpenSubKey(
            $@"{sid}\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{effectiveExtension}\UserChoice"),
            "ProgId");
        foreach (var candidate in ResolveProgIdCandidates(
                     $"{scopeLabel} HKU UserChoice ProgId",
                     sid,
                     hkuClasses,
                     hklmClasses,
                     userChoiceProgId,
                     rawArgument,
                     workingDirectory,
                     originalTarget.EnvironmentVariables,
                     originalTarget.HideWindow,
                     originalTarget.SuppressStartupFeedback,
                     rejectUserProfileHandlers))
        {
            yield return candidate;
        }

        string? userProgId = null;
        if (hkuClasses != null)
        {
            using var userExtensionKey = hkuClasses.OpenSubKey(effectiveExtension);
            userProgId = ReadStringValue(userExtensionKey);
            foreach (var candidate in ResolveProgIdCandidates(
                         $"{scopeLabel} HKU extension ProgId",
                         sid,
                         hkuClasses,
                         hklmClasses,
                         userProgId,
                         rawArgument,
                         workingDirectory,
                         originalTarget.EnvironmentVariables,
                         originalTarget.HideWindow,
                         originalTarget.SuppressStartupFeedback,
                         rejectUserProfileHandlers))
            {
                yield return candidate;
            }
        }

        if (!string.IsNullOrEmpty(userProgId))
            yield break;

        using var machineExtensionKey = hklmClasses?.OpenSubKey(effectiveExtension);
        var machineProgId = ReadStringValue(machineExtensionKey);
        foreach (var candidate in ResolveProgIdCandidates(
                     $"{scopeLabel} HKLM extension ProgId",
                     sid,
                     null,
                     hklmClasses,
                     machineProgId,
                     rawArgument,
                     workingDirectory,
                     originalTarget.EnvironmentVariables,
                     originalTarget.HideWindow,
                     originalTarget.SuppressStartupFeedback,
                     rejectUserProfileHandlers))
        {
            yield return candidate;
        }
    }

    public IEnumerable<AssociationRegistryCommandCandidate> ResolveUrlCandidates(
        string sid,
        string url,
        bool rejectUserProfileHandlers)
    {
        if (!TryGetUrlScheme(url, out var scheme))
            yield break;

        using var usersRoot = hkuRootProvider.OpenUsersRoot();
        using var hkuClasses = usersRoot.OpenSubKey($@"{sid}\Software\Classes");
        using var hklmClasses = hklmClassesRootProvider.OpenClassesRoot();

        var scopeLabel = BuildScopeLabel(sid, rejectUserProfileHandlers);

        var userChoiceProgId = ReadStringValue(usersRoot.OpenSubKey(
            $@"{sid}\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\{scheme}\UserChoice"),
            "ProgId");
        foreach (var candidate in ResolveProgIdCandidates(
                     $"{scopeLabel} HKU URL UserChoice ProgId",
                     sid,
                     hkuClasses,
                     hklmClasses,
                     userChoiceProgId,
                     url,
                     workingDirectory: null,
                     environmentVariables: null,
                     hideWindow: false,
                     suppressStartupFeedback: false,
                     rejectUserProfileHandlers))
        {
            yield return candidate;
        }

        if (hkuClasses != null)
        {
            using var protocolKey = hkuClasses.OpenSubKey(scheme);
            if (protocolMarkerReader.HasUrlProtocolMarker(protocolKey))
            {
                using var userCommandKey = protocolKey!.OpenSubKey(@"shell\open\command");
                foreach (var candidate in BuildCommandCandidates(
                             sourceLabel: $"{scopeLabel} HKU protocol command",
                             resolutionSid: sid,
                             progId: null,
                             commandKey: userCommandKey,
                             rawArgument: url,
                             workingDirectory: null,
                             environmentVariables: null,
                             hideWindow: false,
                             suppressStartupFeedback: false,
                             rejectUserProfileHandlers: rejectUserProfileHandlers,
                             allowDelegateExecuteFallback: true))
                {
                    yield return candidate;
                }
            }
        }

        using var machineCommandKey = hklmClasses?.OpenSubKey($@"{scheme}\shell\open\command");
        foreach (var candidate in BuildCommandCandidates(
                     sourceLabel: $"{scopeLabel} HKLM protocol command",
                     resolutionSid: sid,
                     progId: null,
                     commandKey: machineCommandKey,
                     rawArgument: url,
                     workingDirectory: null,
                     environmentVariables: null,
                     hideWindow: false,
                     suppressStartupFeedback: false,
                     rejectUserProfileHandlers: rejectUserProfileHandlers,
                     allowDelegateExecuteFallback: true))
        {
            yield return candidate;
        }
    }

    private IEnumerable<AssociationRegistryCommandCandidate> ResolveProgIdCandidates(
        string scopeLabel,
        string resolutionSid,
        IRegistryKey? hkuClasses,
        IRegistryKey? hklmClasses,
        string? progId,
        string rawArgument,
        string? workingDirectory,
        Dictionary<string, string>? environmentVariables,
        bool hideWindow,
        bool suppressStartupFeedback,
        bool rejectUserProfileHandlers)
    {
        if (string.IsNullOrWhiteSpace(progId))
        {
            log.Debug($"LaunchTargetResolver: {scopeLabel} for '{rawArgument}' has no ProgId.");
            yield break;
        }

        log.Debug($"LaunchTargetResolver: considering {scopeLabel} '{progId}' for '{rawArgument}'.");

        using var userCommandKey = hkuClasses?.OpenSubKey($@"{progId}\shell\open\command");
        foreach (var candidate in BuildCommandCandidates(
                     sourceLabel: $"{scopeLabel} -> HKU command",
                     resolutionSid: resolutionSid,
                     progId: progId,
                     commandKey: userCommandKey,
                     rawArgument: rawArgument,
                     workingDirectory: workingDirectory,
                     environmentVariables: environmentVariables,
                     hideWindow: hideWindow,
                     suppressStartupFeedback: suppressStartupFeedback,
                     rejectUserProfileHandlers: rejectUserProfileHandlers,
                     allowDelegateExecuteFallback: PathHelper.IsUrlScheme(rawArgument)))
        {
            yield return candidate;
        }

        using var machineCommandKey = hklmClasses?.OpenSubKey($@"{progId}\shell\open\command");
        foreach (var candidate in BuildCommandCandidates(
                     sourceLabel: $"{scopeLabel} -> HKLM command",
                     resolutionSid: resolutionSid,
                     progId: progId,
                     commandKey: machineCommandKey,
                     rawArgument: rawArgument,
                     workingDirectory: workingDirectory,
                     environmentVariables: environmentVariables,
                     hideWindow: hideWindow,
                     suppressStartupFeedback: suppressStartupFeedback,
                     rejectUserProfileHandlers: rejectUserProfileHandlers,
                     allowDelegateExecuteFallback: PathHelper.IsUrlScheme(rawArgument)))
        {
            yield return candidate;
        }
    }

    private static string? ReadStringValue(IRegistryKey? key, string? valueName = null)
    {
        using (key)
            return key?.GetValue(valueName) as string;
    }

    private static IEnumerable<AssociationRegistryCommandCandidate> BuildCommandCandidates(
        string sourceLabel,
        string resolutionSid,
        string? progId,
        IRegistryKey? commandKey,
        string rawArgument,
        string? workingDirectory,
        Dictionary<string, string>? environmentVariables,
        bool hideWindow,
        bool suppressStartupFeedback,
        bool rejectUserProfileHandlers,
        bool allowDelegateExecuteFallback)
    {
        using (commandKey)
        {
            var registryCommand = commandKey?.GetValue(null) as string;
            var delegateExecute = allowDelegateExecuteFallback
                ? commandKey?.GetValue("DelegateExecute") as string
                : null;

            if (!string.IsNullOrWhiteSpace(registryCommand))
            {
                yield return new AssociationRegistryCommandCandidate(
                    SourceLabel: sourceLabel,
                    ResolutionSid: resolutionSid,
                    ProgId: progId,
                    RegistryCommand: registryCommand,
                    RawArgument: rawArgument,
                    WorkingDirectory: workingDirectory,
                    EnvironmentVariables: environmentVariables,
                    HideWindow: hideWindow,
                    SuppressStartupFeedback: suppressStartupFeedback,
                    RejectUserProfileHandlers: rejectUserProfileHandlers,
                    DelegateExecuteClsid: null);
            }

            if (!string.IsNullOrWhiteSpace(delegateExecute))
            {
                yield return new AssociationRegistryCommandCandidate(
                    SourceLabel: $"{sourceLabel} (DelegateExecute fallback)",
                    ResolutionSid: resolutionSid,
                    ProgId: progId,
                    RegistryCommand: null,
                    RawArgument: rawArgument,
                    WorkingDirectory: workingDirectory,
                    EnvironmentVariables: environmentVariables,
                    HideWindow: hideWindow,
                    SuppressStartupFeedback: suppressStartupFeedback,
                    RejectUserProfileHandlers: rejectUserProfileHandlers,
                    DelegateExecuteClsid: delegateExecute);
            }

            if (string.IsNullOrWhiteSpace(registryCommand) && string.IsNullOrWhiteSpace(delegateExecute))
            {
                yield return new AssociationRegistryCommandCandidate(
                    SourceLabel: sourceLabel,
                    ResolutionSid: resolutionSid,
                    ProgId: progId,
                    RegistryCommand: null,
                    RawArgument: rawArgument,
                    WorkingDirectory: workingDirectory,
                    EnvironmentVariables: environmentVariables,
                    HideWindow: hideWindow,
                    SuppressStartupFeedback: suppressStartupFeedback,
                    RejectUserProfileHandlers: rejectUserProfileHandlers,
                    DelegateExecuteClsid: null);
            }
        }
    }

    private static string ResolveAssociationWorkingDirectory(ProcessLaunchTarget target)
        => string.IsNullOrEmpty(target.WorkingDirectory)
            ? Path.GetDirectoryName(target.ExePath) ?? Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            : target.WorkingDirectory;

    private static bool TryGetUrlScheme(string url, out string scheme)
    {
        scheme = string.Empty;
        if (!PathHelper.IsUrlScheme(url))
            return false;

        var colonIndex = url.IndexOf(':');
        if (colonIndex <= 1)
            return false;

        scheme = url[..colonIndex];
        return true;
    }

    private static string BuildScopeLabel(string sid, bool rejectUserProfileHandlers)
        => rejectUserProfileHandlers
            ? $"interactive fallback SID {sid}"
            : $"target-user SID {sid}";
}

public sealed record AssociationRegistryCommandCandidate(
    string SourceLabel,
    string ResolutionSid,
    string? ProgId,
    string? RegistryCommand,
    string RawArgument,
    string? WorkingDirectory,
    Dictionary<string, string>? EnvironmentVariables,
    bool HideWindow,
    bool SuppressStartupFeedback,
    bool RejectUserProfileHandlers,
    string? DelegateExecuteClsid);
