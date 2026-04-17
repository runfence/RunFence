using Autofac;
using Autofac.Extras.Ordering;
using RunFence.Infrastructure;
using RunFence.Licensing;
using RunFence.Licensing.UI;

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

        builder.RegisterType<MessageBoxEvaluationLimitPrompt>()
            .As<IEvaluationLimitPrompt>()
            .SingleInstance();

        builder.RegisterType<EvaluationLimitHelper>()
            .As<IEvaluationLimitHelper>()
            .SingleInstance();
    }
}