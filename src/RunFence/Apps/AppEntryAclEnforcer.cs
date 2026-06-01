using RunFence.Acl;
using RunFence.Core.Models;

namespace RunFence.Apps;

public class AppEntryAclEnforcer(IAclService aclService)
{
    public void Apply(AppEntry app, IReadOnlyList<AppEntry> allApps)
    {
        if (app is { RestrictAcl: true, IsUrlScheme: false })
            aclService.ApplyAcl(app, allApps);
    }

    public void Revert(AppEntry app, IReadOnlyList<AppEntry> allApps)
    {
        if (app is { RestrictAcl: true, IsUrlScheme: false })
            aclService.RevertAcl(app, allApps);
    }
}
