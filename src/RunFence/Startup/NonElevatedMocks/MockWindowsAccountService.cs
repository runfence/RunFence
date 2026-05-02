using RunFence.Account;
using RunFence.Core;

namespace RunFence.Startup.NonElevatedMocks;

public sealed class MockWindowsAccountService(IWindowsAccountService real, NonElevatedMockStore store) : IWindowsAccountService
{
    public string CreateLocalUser(string username, ProtectedString password)
    {
        var sid = store.DeriveFakeSid(username, ridBase: 20001);
        store.AddUser(sid, username);
        return sid;
    }

    public string? ValidatePassword(string sid, ProtectedString password, string usernameFallback) => null;

    public string? GetProfilePath(string sid)
        => store.IsFakeUser(sid)
            ? Path.Combine(PathConstants.LocalAppDataDir, "DebugProfiles", sid)
            : real.GetProfilePath(sid);

    public void RenameAccount(string sid, string currentUsername, string newUsername)
        => store.RenameUser(sid, newUsername);

    public void DeleteUser(string sid) => store.RemoveUser(sid);
}
