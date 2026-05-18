using RunFence.Core.Models;
using RunFence.Launching.Environment;

namespace RunFence.Launch.Container;

public interface IAppContainerEnvironmentSetup
{
    EnvironmentBlock CreateLaunchEnvironment(
        IntPtr explorerToken,
        AppContainerEntry entry,
        string containerSid,
        string exePath);
    AppContainerProfileSetupResult OverrideProfileEnvironment(
        IntPtr originalEnv,
        string profileName,
        out IntPtr rewrittenEnvironment);
    void TryGrantVirtualStoreAccess(string containerSid, string localAppData);
    void TryRevokeVirtualStoreAccess(string containerSid, string localAppData);
    void TryCreateVirtualStoreShortcut(string exePath, string containerName, string localAppData);
    AppContainerProfileSetupResult WriteShellFolderRedirects(string containerName, string interactiveUserSid);
}
