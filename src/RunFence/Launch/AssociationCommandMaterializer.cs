using RunFence.Core;
using RunFence.Core.Helpers;

namespace RunFence.Launch;

public class AssociationCommandMaterializer(
    ILoggingService log,
    IAssociationExecutablePathResolver executablePathResolver)
{
    public AssociationCommandResolution? TryMaterialize(AssociationRegistryCommandCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.DelegateExecuteClsid))
        {
            if (!PathHelper.IsUrlScheme(candidate.RawArgument))
            {
                LogCommandResolutionReject(
                    candidate,
                    candidate.RegistryCommand,
                    $"DelegateExecute '{candidate.DelegateExecuteClsid}' is only supported for URL associations");
                return null;
            }

            var wrappedTarget = ProcessLaunchHelper.BuildUrlLaunchTarget(candidate.RawArgument) with
            {
                SuppressStartupFeedback = candidate.SuppressStartupFeedback
            };
            log.Debug(
                $"LaunchTargetResolver: accepted {candidate.SourceLabel} candidate for '{candidate.RawArgument}'"
                + $"{AssociationLogHelper.FormatProgId(candidate.ProgId)} via DelegateExecute '{candidate.DelegateExecuteClsid}'."
                + $" WrappedTarget='{wrappedTarget.ExePath} {wrappedTarget.Arguments}'.");
            return new AssociationCommandResolution(
                Target: wrappedTarget,
                MaterializedCommand: $"{wrappedTarget.ExePath} {wrappedTarget.Arguments}",
                LauncherAssociation: null,
                LauncherArgument: null);
        }

        if (string.IsNullOrWhiteSpace(candidate.RegistryCommand))
        {
            LogCommandResolutionReject(candidate, candidate.RegistryCommand, "command is empty");
            return null;
        }

        if (AssociationCommandHelper.IsRunFenceProgId(candidate.ProgId))
        {
            log.Debug(
                $"LaunchTargetResolver: {candidate.SourceLabel} for '{candidate.RawArgument}' uses RunFence ProgId '{candidate.ProgId}', evaluating command target instead of rejecting by ProgId alone.");
        }

        if (!AssociationCommandHelper.TryMaterializeCommand(
                candidate.RegistryCommand,
                candidate.RawArgument,
                out var materialization,
                out var rejectionReason))
        {
            LogCommandResolutionReject(candidate, candidate.RegistryCommand, rejectionReason);
            return null;
        }

        var executableResolution = executablePathResolver.Resolve(materialization.ExePath);
        if (!executableResolution.IsValid)
        {
            LogCommandResolutionReject(candidate, materialization.MaterializedCommand, executableResolution.RejectionReason);
            return null;
        }

        var exePath = executableResolution.ExePath;
        var materializedCommand = materialization.MaterializedCommand;
        if (executableResolution.WasRepaired)
        {
            var quotedExePath = CommandLineHelper.QuoteProcessArgument(exePath);
            materializedCommand = string.IsNullOrEmpty(materialization.Arguments)
                ? quotedExePath
                : $"{quotedExePath} {materialization.Arguments}";
        }

        if (string.Equals(exePath, candidate.RawArgument, StringComparison.OrdinalIgnoreCase))
        {
            LogCommandResolutionReject(candidate, materializedCommand, "executable resolves to the raw argument");
            return null;
        }

        var isAssociationLauncher = AssociationCommandHelper.TryParseRunFenceAssociationLauncherCommand(
            materializedCommand,
            out var launcherAssociation,
            out var launcherArgument);

        if (!isAssociationLauncher && AssociationCommandHelper.IsRunFenceExecutablePath(exePath))
        {
            LogCommandResolutionReject(
                candidate,
                materializedCommand,
                "command resolves to a non-association RunFence executable");
            return null;
        }

        if (candidate.RejectUserProfileHandlers && AssociationLaunchPathHelper.IsUnderUsersRoot(exePath))
        {
            LogCommandResolutionReject(
                candidate,
                materializedCommand,
                "interactive fallback rejected handler under C:\\Users");
            return null;
        }

        var target = new ProcessLaunchTarget(
            ExePath: exePath,
            Arguments: materialization.Arguments,
            WorkingDirectory: candidate.WorkingDirectory,
            EnvironmentVariables: candidate.EnvironmentVariables,
            HideWindow: candidate.HideWindow,
            SuppressStartupFeedback: candidate.SuppressStartupFeedback);

        if (!isAssociationLauncher)
        {
            log.Debug(
                $"LaunchTargetResolver: accepted {candidate.SourceLabel} candidate for '{candidate.RawArgument}'"
                + $"{AssociationLogHelper.FormatProgId(candidate.ProgId)} -> exe '{target.ExePath}', args '{target.Arguments ?? string.Empty}'."
                + $" RegistryCommand='{candidate.RegistryCommand}'. MaterializedCommand='{materializedCommand}'.");
        }

        return new AssociationCommandResolution(
            Target: target,
            MaterializedCommand: materializedCommand,
            LauncherAssociation: isAssociationLauncher ? launcherAssociation : null,
            LauncherArgument: isAssociationLauncher ? launcherArgument : null);
    }

    private void LogCommandResolutionReject(
        AssociationRegistryCommandCandidate candidate,
        string? command,
        string reason)
        => log.Debug(
            $"LaunchTargetResolver: rejected {candidate.SourceLabel} candidate for '{candidate.RawArgument}'"
            + $"{AssociationLogHelper.FormatProgId(candidate.ProgId)}: {reason}. Command='{command ?? string.Empty}'.");
}

public sealed record AssociationCommandResolution(
    ProcessLaunchTarget Target,
    string MaterializedCommand,
    string? LauncherAssociation,
    string? LauncherArgument);
