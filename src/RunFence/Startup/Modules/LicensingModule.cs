using Autofac;
using Autofac.Extras.Ordering;
using RunFence.Infrastructure;
using RunFence.Licensing;

namespace RunFence.Startup.Modules;

public class LicensingModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<MachineIdProvider>()
            .As<IMachineIdProvider>()
            .SingleInstance();

        builder.RegisterType<LicenseValidator>()
            .UsingConstructor()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<LicenseService>()
            .As<ILicenseService>()
            .As<IRequiresInitialization>()
            .OrderBy(0)
            .SingleInstance();
    }
}