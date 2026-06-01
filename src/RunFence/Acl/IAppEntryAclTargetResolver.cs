using RunFence.Core.Models;

namespace RunFence.Acl;

public interface IAppEntryAclTargetResolver
{
    string ResolveTargetPath(AppEntry app);
}
