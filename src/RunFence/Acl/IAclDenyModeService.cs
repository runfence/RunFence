using RunFence.Core.Models;

namespace RunFence.Acl;

public interface IAclDenyModeService
{
    HashSet<string> GetAllowedSidsForPath(string targetPath, IReadOnlyList<AppEntry> allApps,
        bool isFolderTarget, Func<AppEntry, string> resolveAclTargetPath);

    Dictionary<string, DeniedRights> GetDeniedRightsPerSid(string targetPath,
        IReadOnlyList<AppEntry> allApps, bool isFolderTarget, Func<AppEntry, string> resolveAclTargetPath);

    bool ApplyDeny(string path, bool isFolder, HashSet<string> allowedSids, DeniedRights deniedRights);

    bool ApplyDenyToFolderPerSid(string folderPath, Dictionary<string, DeniedRights> deniedRightsPerSid);

    void RemoveManagedDenyAces(string path, bool isFolder);
}
