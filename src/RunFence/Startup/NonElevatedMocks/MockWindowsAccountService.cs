using RunFence.Account;
using RunFence.Core;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class MockWindowsAccountService(NonElevatedMockStore store) : IWindowsAccountService
{
    public string CreateLocalUser(string username, ProtectedString password)
    {
        var sid = store.CreateUserSid(username);
        store.AddUser(sid, username);
        return sid;
    }

    public void RenameAccount(string sid, string currentUsername, string newUsername)
        => store.RenameUser(sid, newUsername);

    public void DeleteSamAccount(string sid) => store.RemoveUser(sid);
}
