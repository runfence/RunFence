using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Persistence.UI;

public class ConfigSaveOrchestrator(
    ISessionProvider sessionProvider,
    Func<IUiThreadInvoker> uiThreadInvokerFactory,
    IDatabaseService databaseService,
    IAppConfigService appConfigService,
    IHandlerMappingService handlerMappingService)
{
    public void SaveMainConfig()
        => uiThreadInvokerFactory().Invoke(() =>
        {
            var session = sessionProvider.GetSession();
            databaseService.SaveConfig(
                session.Database,
                session.PinDerivedKey,
                session.CredentialStore.ArgonSalt);
        });

    public void SaveAdditionalConfig(
        string configPath,
        List<AppConfigAccountEntry> accounts)
        => uiThreadInvokerFactory().Invoke(() =>
        {
            var session = sessionProvider.GetSession();
            var normalizedPath = Path.GetFullPath(configPath);
            var appConfig = new AppConfig
            {
                Apps = appConfigService.GetAppsForConfig(normalizedPath, session.Database),
                Accounts = accounts.Count == 0
                    ? null
                    : accounts.Select(account => new AppConfigAccountEntry
                    {
                        Sid = account.Sid,
                        Grants = account.Grants.Select(grant => grant.Clone()).ToList()
                    }).ToList(),
                HandlerMappings = handlerMappingService.GetHandlerMappingsForConfig(normalizedPath)
            };

            databaseService.SaveAppConfig(
                appConfig,
                normalizedPath,
                session.PinDerivedKey,
                session.CredentialStore.ArgonSalt);
        });

    public void SaveSecurityFindingsHash()
        => SaveMainConfig();

    public void SaveConfigAfterEnforcement(AppDatabase database)
        => uiThreadInvokerFactory().Invoke(() =>
        {
            var session = sessionProvider.GetSession();
            appConfigService.SaveAllConfigs(
                database,
                session.PinDerivedKey,
                session.CredentialStore.ArgonSalt);
        });
}
