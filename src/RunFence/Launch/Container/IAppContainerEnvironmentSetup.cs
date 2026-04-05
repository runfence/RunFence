namespace RunFence.Launch.Container;

public interface IAppContainerEnvironmentSetup
{
    IntPtr OverrideProfileEnvironment(IntPtr originalEnv, string profileName);
    void TryGrantVirtualStoreAccess(IntPtr pContainerSid, string localAppData);
    void TryRevokeVirtualStoreAccess(string containerSid, string localAppData);
    void TryCreateVirtualStoreShortcut(string exePath, string containerName, string localAppData);
    void WriteShellFolderRedirects(string containerName);
}
