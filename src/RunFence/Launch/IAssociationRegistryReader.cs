namespace RunFence.Launch;

public interface IAssociationRegistryReader
{
    IEnumerable<AssociationRegistryCommandCandidate> ResolveFileCandidates(
        string sid,
        ProcessLaunchTarget originalTarget,
        bool rejectUserProfileHandlers,
        string? extension = null);

    IEnumerable<AssociationRegistryCommandCandidate> ResolveUrlCandidates(
        string sid,
        string url,
        bool rejectUserProfileHandlers);
}
