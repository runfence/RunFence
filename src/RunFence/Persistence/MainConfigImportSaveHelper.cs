using RunFence.Core.Models;

namespace RunFence.Persistence;

public class MainConfigImportSaveHelper(IAppConfigService appConfigService)
{
    public void Save(SessionContext session, AppDatabase database)
    {
        using var scope = session.PinDerivedKey.Unprotect();
        appConfigService.ReencryptAndSaveAll(session.CredentialStore, database, scope.Data);
    }
}
