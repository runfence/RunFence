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
        builder.RegisterType<LicenseValidator>()
            .UsingConstructor()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<LicenseFileStore>()
            .As<ILicenseStore>()
            .SingleInstance();

        builder.RegisterType<LicenseValidationService>()
            .As<ILicenseValidator>()
            .SingleInstance();

        builder.RegisterType<LicenseEvaluationPolicy>()
            .As<ILicenseEvaluationPolicy>()
            .SingleInstance();

        builder.RegisterType<FeatureRestrictionService>()
            .As<IFeatureRestrictionService>()
            .SingleInstance();

        builder.RegisterType<LicenseMessageFormatter>()
            .As<ILicenseMessageFormatter>()
            .SingleInstance();

        builder.RegisterType<LicenseService>()
            .As<ILicenseService>()
            .As<IRequiresInitialization>()
            .WithMetadata("Order", 0)
            .OrderBy(0)
            .SingleInstance();

        builder.RegisterType<MessageBoxEvaluationLimitPrompt>()
            .As<IEvaluationLimitPrompt>()
            .SingleInstance();

        builder.RegisterType<EvaluationCredentialCounter>()
            .As<IEvaluationCredentialCounter>()
            .SingleInstance();

        builder.RegisterType<EvaluationLimitHelper>()
            .As<IEvaluationLimitHelper>()
            .SingleInstance();
    }
}
