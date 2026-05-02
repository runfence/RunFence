using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.Launch;

public class AssociationRegistryResolver(
    ILoggingService log,
    RegistryKey? hklmOverride = null,
    RegistryKey? hkuOverride = null)
{
    private readonly RegistryKey _hklm = hklmOverride ?? Registry.LocalMachine;
    private readonly RegistryKey _hku = hkuOverride ?? Registry.Users;

    public IEnumerable<AssociationRegistryCommandCandidate> ResolveFileCandidates(
        string sid,
        ProcessLaunchTarget originalTarget,
        bool rejectUserProfileHandlers)
    {
        var extension = Path.GetExtension(originalTarget.ExePath);
        if (string.IsNullOrEmpty(extension))
            yield break;

        using var hkuClasses = _hku.OpenSubKey($@"{sid}\Software\Classes");
        using var hklmClasses = _hklm.OpenSubKey(@"Software\Classes");

        var scopeLabel = BuildScopeLabel(sid, rejectUserProfileHandlers);
        var rawArgument = originalTarget.ExePath;
        var workingDirectory = ResolveAssociationWorkingDirectory(originalTarget);

        var userChoiceProgId = ReadStringValue(_hku.OpenSubKey(
            $@"{sid}\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\{extension}\UserChoice"),
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
                     rejectUserProfileHandlers))
        {
            yield return candidate;
        }

        string? userProgId = null;
        if (hkuClasses != null)
        {
            using var userExtensionKey = hkuClasses.OpenSubKey(extension);
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
                         rejectUserProfileHandlers))
            {
                yield return candidate;
            }
        }

        if (!string.IsNullOrEmpty(userProgId))
            yield break;

        using var machineExtensionKey = hklmClasses?.OpenSubKey(extension);
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

        using var hkuClasses = _hku.OpenSubKey($@"{sid}\Software\Classes");
        using var hklmClasses = _hklm.OpenSubKey(@"Software\Classes");

        var scopeLabel = BuildScopeLabel(sid, rejectUserProfileHandlers);

        var userChoiceProgId = ReadStringValue(_hku.OpenSubKey(
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
                     rejectUserProfileHandlers))
        {
            yield return candidate;
        }

        if (hkuClasses != null)
        {
            using var protocolKey = hkuClasses.OpenSubKey(scheme);
            if (protocolKey?.GetValue("URL Protocol") != null)
            {
                using var userCommandKey = protocolKey.OpenSubKey(@"shell\open\command");
                yield return new AssociationRegistryCommandCandidate(
                    SourceLabel: $"{scopeLabel} HKU protocol command",
                    ResolutionSid: sid,
                    ProgId: null,
                    RegistryCommand: ReadStringValue(userCommandKey),
                    RawArgument: url,
                    WorkingDirectory: null,
                    EnvironmentVariables: null,
                    HideWindow: false,
                    RejectUserProfileHandlers: rejectUserProfileHandlers);
            }
        }

        using var machineCommandKey = hklmClasses?.OpenSubKey($@"{scheme}\shell\open\command");
        yield return new AssociationRegistryCommandCandidate(
            SourceLabel: $"{scopeLabel} HKLM protocol command",
            ResolutionSid: sid,
            ProgId: null,
            RegistryCommand: ReadStringValue(machineCommandKey),
            RawArgument: url,
            WorkingDirectory: null,
            EnvironmentVariables: null,
            HideWindow: false,
            RejectUserProfileHandlers: rejectUserProfileHandlers);
    }

    private IEnumerable<AssociationRegistryCommandCandidate> ResolveProgIdCandidates(
        string scopeLabel,
        string resolutionSid,
        RegistryKey? hkuClasses,
        RegistryKey? hklmClasses,
        string? progId,
        string rawArgument,
        string? workingDirectory,
        Dictionary<string, string>? environmentVariables,
        bool hideWindow,
        bool rejectUserProfileHandlers)
    {
        if (string.IsNullOrWhiteSpace(progId))
        {
            log.Debug($"LaunchTargetResolver: {scopeLabel} for '{rawArgument}' has no ProgId.");
            yield break;
        }

        log.Debug($"LaunchTargetResolver: considering {scopeLabel} '{progId}' for '{rawArgument}'.");

        using var userCommandKey = hkuClasses?.OpenSubKey($@"{progId}\shell\open\command");
        yield return new AssociationRegistryCommandCandidate(
            SourceLabel: $"{scopeLabel} -> HKU command",
            ResolutionSid: resolutionSid,
            ProgId: progId,
            RegistryCommand: ReadStringValue(userCommandKey),
            RawArgument: rawArgument,
            WorkingDirectory: workingDirectory,
            EnvironmentVariables: environmentVariables,
            HideWindow: hideWindow,
            RejectUserProfileHandlers: rejectUserProfileHandlers);

        using var machineCommandKey = hklmClasses?.OpenSubKey($@"{progId}\shell\open\command");
        yield return new AssociationRegistryCommandCandidate(
            SourceLabel: $"{scopeLabel} -> HKLM command",
            ResolutionSid: resolutionSid,
            ProgId: progId,
            RegistryCommand: ReadStringValue(machineCommandKey),
            RawArgument: rawArgument,
            WorkingDirectory: workingDirectory,
            EnvironmentVariables: environmentVariables,
            HideWindow: hideWindow,
            RejectUserProfileHandlers: rejectUserProfileHandlers);
    }

    private static string? ReadStringValue(RegistryKey? key, string? valueName = null)
    {
        using (key)
            return key?.GetValue(valueName) as string;
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
    bool RejectUserProfileHandlers);
