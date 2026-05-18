using RunFence.Core;

namespace RunFence.Account;

public interface IWindowsAccountService
{
    void DeleteSamAccount(string sid);
    string CreateLocalUser(string username, ProtectedString password);
    void RenameAccount(string sid, string currentUsername, string newUsername);
}
