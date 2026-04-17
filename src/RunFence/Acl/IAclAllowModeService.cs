using RunFence.Core.Models;

namespace RunFence.Acl;

public interface IAclAllowModeService
{
    bool ApplyAllowAcl(AppEntry app, string targetPath);

    void RevertAllowAcl(string targetPath, AppEntry app);

    void CleanupAllowModeAces(string targetPath, bool isFolder);
}
