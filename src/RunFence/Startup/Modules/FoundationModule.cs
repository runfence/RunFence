using Autofac;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Persistence;
using RunFence.Security;
using RunFence.Startup.UI;

namespace RunFence.Startup.Modules;

public class FoundationModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<LoggingService>().As<ILoggingService>().SingleInstance();
        builder.RegisterType<NTTranslateApi>().SingleInstance();
        builder.RegisterType<GroupMembershipApi>().SingleInstance();
        builder.RegisterType<CredentialEncryptionService>().As<ICredentialEncryptionService>().SingleInstance();
        builder.RegisterType<PinService>().As<IPinService>().SingleInstance();
        builder.RegisterType<AppConfigIndex>().As<IAppFilter>().AsSelf().SingleInstance();
        builder.RegisterType<AppConfigService>().As<IAppConfigService>().SingleInstance();
        builder.RegisterType<DatabaseService>()
            .As<IDatabaseService>().As<IConfigRepository>().As<ICredentialRepository>().SingleInstance();
        builder.RegisterType<GrantConfigTracker>().As<IGrantConfigTracker>().AsSelf().SingleInstance();
        builder.RegisterType<HandlerMappingService>().As<IHandlerMappingService>().AsSelf().SingleInstance();
        builder.RegisterType<SidResolver>().As<ISidResolver>().SingleInstance();
        builder.RegisterType<AppInitializationHelper>().As<IAppInitializationHelper>().SingleInstance();
        builder.RegisterType<SecureDesktopHelper>().As<ISecureDesktopRunner>().SingleInstance();
        builder.RegisterType<StartupUI>().As<IStartupUI>().SingleInstance();
        builder.RegisterType<SessionProvider>()
            .As<ISessionProvider>().As<IDatabaseProvider>().AsSelf().SingleInstance();

        builder.RegisterType<SidDisplayNameResolver>()
            .AsSelf()
            .SingleInstance();
    }
}