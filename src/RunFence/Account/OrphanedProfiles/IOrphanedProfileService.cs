namespace RunFence.Account.OrphanedProfiles;

public record OrphanedProfile(string? Sid, string ProfilePath)
{
    public override string ToString() => ProfilePath;
}

public interface IOrphanedProfileService
{
    List<OrphanedProfile> GetOrphanedProfiles();
    (List<string> Deleted, List<(string Path, string Error)> Failed) DeleteProfiles(IEnumerable<OrphanedProfile> profiles);
    void CleanupLogonScripts(string sid);
}