using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;

namespace RunFence.Launch;

public sealed class AssociationLaunchCandidateResolver(
    IAssociationRegistryReader associationRegistryReader,
    AssociationCommandMaterializer associationCommandMaterializer,
    IAssociationLaunchResolver associationLaunchResolver,
    ILoggingService log)
{
    public ProcessLaunchTarget? ResolveForSid(
        string sid,
        AssociationResolutionRequest request,
        AppDatabase databaseSnapshot,
        AssociationResolutionPolicy associationResolutionPolicy,
        bool rejectUserProfileHandlers)
    {
        var candidates = request.Kind switch
        {
            AssociationLaunchKind.File => associationRegistryReader.ResolveFileCandidates(
                sid,
                request.FileTarget!,
                rejectUserProfileHandlers,
                request.Extension),
            AssociationLaunchKind.Url => associationRegistryReader.ResolveUrlCandidates(
                sid,
                request.RawArgument,
                rejectUserProfileHandlers),
            _ => []
        };

        foreach (var candidate in candidates)
        {
            var materialized = associationCommandMaterializer.TryMaterialize(candidate);
            if (materialized == null)
                continue;

            if (materialized.LauncherAssociation != null)
            {
                if (associationResolutionPolicy != AssociationResolutionPolicy.AllowAccountRedirection)
                {
                    var resolved = associationLaunchResolver.Resolve(
                        databaseSnapshot,
                        AssociationLaunchRequest.Build(materialized.LauncherAssociation, materialized.LauncherArgument),
                        callerIdentity: null,
                        callerSid: candidate.ResolutionSid,
                        identityFromImpersonation: true);

                    if (resolved.App != null
                        && !string.Equals(resolved.App.AccountSid, candidate.ResolutionSid, StringComparison.OrdinalIgnoreCase))
                    {
                        LogCommandResolutionReject(candidate, materialized.MaterializedCommand,
                            $"RunFence association launcher resolves to account SID '{resolved.App.AccountSid}' instead of '{candidate.ResolutionSid}'");
                        continue;
                    }
                }

                log.Debug(
                    $"LaunchTargetResolver: accepted {candidate.SourceLabel} candidate for '{candidate.RawArgument}'"
                    + $"{AssociationLogHelper.FormatProgId(candidate.ProgId)} as RunFence association launcher."
                    + $" RegistryCommand='{candidate.RegistryCommand}'. MaterializedCommand='{materialized.MaterializedCommand}'.");
            }

            return materialized.Target;
        }

        return null;
    }

    private void LogCommandResolutionReject(
        AssociationRegistryCommandCandidate candidate,
        string? command,
        string reason)
        => log.Debug(
            $"LaunchTargetResolver: rejected {candidate.SourceLabel} candidate for '{candidate.RawArgument}'"
            + $"{AssociationLogHelper.FormatProgId(candidate.ProgId)}: {reason}. Command='{command ?? string.Empty}'.");
}
