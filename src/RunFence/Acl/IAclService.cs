using RunFence.Core.Models;

namespace RunFence.Acl;

public interface IAclService
{
    void ApplyAcl(AppEntry app, IReadOnlyList<AppEntry> allApps);
    void RevertAcl(AppEntry app, IReadOnlyList<AppEntry> allApps);
    void RecomputeAllAncestorAcls(IReadOnlyList<AppEntry> allApps);
    bool IsBlockedPath(string resolvedPath);
    string ResolveAclTargetPath(AppEntry app);
}