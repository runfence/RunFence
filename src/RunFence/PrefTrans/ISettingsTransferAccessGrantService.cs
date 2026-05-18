using System.Security.AccessControl;

namespace RunFence.PrefTrans;

public interface ISettingsTransferAccessGrantService
{
    SettingsTransferGrantResult TryEnsureDurableAccess(
        string sid,
        string path,
        FileSystemRights rights);

    SettingsTransferGrantResult TryEnsureAccess(
        string sid,
        string path,
        FileSystemRights rights,
        bool isDirectory);

    SettingsTransferGrantResult TryEnsureAccessForCleanup(
        string sid,
        string path,
        FileSystemRights rights,
        bool isDirectory);

    void CleanupTemporaryGrant();
}
