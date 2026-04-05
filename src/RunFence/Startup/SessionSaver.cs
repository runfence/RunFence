using RunFence.Account;
using RunFence.Infrastructure;

namespace RunFence.Startup;

public class SessionSaver(IAccountCredentialManager credentialManager, ISessionProvider sessionProvider) : ISessionSaver
{
    public void SaveConfig()
    {
        var session = sessionProvider.GetSession();
        credentialManager.SaveConfig(session.Database, session.PinDerivedKey, session.CredentialStore.ArgonSalt);
    }
}