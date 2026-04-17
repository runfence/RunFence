using RunFence.Account;
using RunFence.Infrastructure;

namespace RunFence.Startup;

public class SessionSaver(SessionPersistenceHelper persistenceHelper, ISessionProvider sessionProvider) : ISessionSaver
{
    public void SaveConfig()
    {
        var session = sessionProvider.GetSession();
        persistenceHelper.SaveConfig(session.Database, session.PinDerivedKey, session.CredentialStore.ArgonSalt);
    }
}
