using RunFence.Core;
using RunFence.Core.Helpers;

namespace RunFence.Launch;

public class AssociationCommandMaterializer(ILoggingService log)
{
    public AssociationCommandResolution? TryMaterialize(AssociationRegistryCommandCandidate candidate)
    {
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

        if (string.Equals(materialization.ExePath, candidate.RawArgument, StringComparison.OrdinalIgnoreCase))
        {
            LogCommandResolutionReject(candidate, materialization.MaterializedCommand, "executable resolves to the raw argument");
            return null;
        }

        var isAssociationLauncher = AssociationCommandHelper.TryParseRunFenceAssociationLauncherCommand(
            materialization.MaterializedCommand,
            out var launcherAssociation,
            out var launcherArgument);

        if (!isAssociationLauncher && AssociationCommandHelper.IsRunFenceExecutablePath(materialization.ExePath))
        {
            LogCommandResolutionReject(
                candidate,
                materialization.MaterializedCommand,
                "command resolves to a non-association RunFence executable");
            return null;
        }

        if (candidate.RejectUserProfileHandlers && AssociationLaunchPathHelper.IsUnderUsersRoot(materialization.ExePath))
        {
            LogCommandResolutionReject(
                candidate,
                materialization.MaterializedCommand,
                "interactive fallback rejected handler under C:\\Users");
            return null;
        }

        var target = new ProcessLaunchTarget(
            ExePath: materialization.ExePath,
            Arguments: materialization.Arguments,
            WorkingDirectory: candidate.WorkingDirectory,
            EnvironmentVariables: candidate.EnvironmentVariables,
            HideWindow: candidate.HideWindow);

        if (!isAssociationLauncher)
        {
            log.Debug(
                $"LaunchTargetResolver: accepted {candidate.SourceLabel} candidate for '{candidate.RawArgument}'"
                + $"{AssociationLogHelper.FormatProgId(candidate.ProgId)} -> exe '{target.ExePath}', args '{target.Arguments ?? string.Empty}'."
                + $" RegistryCommand='{candidate.RegistryCommand}'. MaterializedCommand='{materialization.MaterializedCommand}'.");
        }

        return new AssociationCommandResolution(
            Target: target,
            MaterializedCommand: materialization.MaterializedCommand,
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
