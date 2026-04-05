using Autofac;
using Autofac.Extras.Ordering;
using RunFence.Core.Models;
using RunFence.Startup.Modules;

namespace RunFence.Startup;

/// <summary>
/// Builds the two-phase AutoFac container. Both methods are side-effect-free —
/// no Initialize(), no Start(), no I/O during registration or Build().
/// </summary>
public static class ContainerRegistrationBuilder
{
    public static IContainer BuildFoundationContainer()
    {
        var builder = new ContainerBuilder();
        builder.RegisterSource(new OrderedRegistrationSource());
        builder.RegisterModule<FoundationModule>();
        builder.RegisterModule<AclModule>();
        builder.RegisterModule<LaunchModule>();
        builder.RegisterModule<InfraModule>();
        builder.RegisterModule<AppsModule>();
        return builder.Build();
    }

    public static ILifetimeScope BeginSessionScope(
        IContainer container,
        SessionContext session,
        IStartupOptions options)
    {
        // Set session on the SessionProvider before building the scope so that any
        // OnActivated callbacks that read from ISessionProvider find a valid session.
        var sessionProvider = container.Resolve<SessionProvider>();
        sessionProvider.SetSession(session);

        return container.BeginLifetimeScope(builder =>
        {
            builder.RegisterInstance(session).SingleInstance();
            builder.RegisterInstance(options).As<IStartupOptions>().SingleInstance();
            builder.RegisterModule(new AccountModule());
            builder.RegisterModule(new FirewallModule());
            builder.RegisterModule(new PersistenceModule());
            builder.RegisterModule(new SecurityModule());
            builder.RegisterModule(new LicensingModule());
            builder.RegisterModule(new IpcModule());
            builder.RegisterModule(new RunAsModule());
            builder.RegisterModule(new DragBridgeModule());
            builder.RegisterModule(new WizardModule());
            builder.RegisterModule(new SidMigrationModule());
            builder.RegisterModule(new UiModule());
        });
    }
}