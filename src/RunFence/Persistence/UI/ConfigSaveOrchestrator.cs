using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Persistence.UI;

public class ConfigSaveOrchestrator(
    ISessionProvider sessionProvider,
    IConfigRepository configRepository,
    IAppConfigService appConfigService)
{
    public void SaveSecurityFindingsHash()
    {
        var session = sessionProvider.GetSession();
        using var scope = session.PinDerivedKey.Unprotect();
        configRepository.SaveConfig(session.Database, scope.Data, session.CredentialStore.ArgonSalt);
    }

    public void SaveConfigAfterEnforcement(AppDatabase database)
    {
        var session = sessionProvider.GetSession();
        using var scope = session.PinDerivedKey.Unprotect();
        appConfigService.SaveAllConfigs(database, scope.Data, session.CredentialStore.ArgonSalt);
    }
}
