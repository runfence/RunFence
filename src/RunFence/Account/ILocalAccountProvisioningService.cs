using RunFence.Core;

namespace RunFence.Account;

public interface ILocalAccountProvisioningService
{
    void CreateLocalUser(string username, ProtectedString password);
    int SetDisplayName(string username, string displayName);
    int RenameLocalUser(string currentUsername, string newUsername);
    void DeleteLocalUserBySid(string sid);
    void DeleteLocalUserByName(string username);
}
