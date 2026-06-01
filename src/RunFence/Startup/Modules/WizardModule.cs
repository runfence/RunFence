using Autofac;
using RunFence.Apps.UI;
using RunFence.Infrastructure;
using RunFence.Startup;
using RunFence.Wizard;
using RunFence.Wizard.Templates;
using RunFence.Wizard.UI;

namespace RunFence.Startup.Modules;

public class WizardModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<WizardLauncher>()
            .As<IWizardLauncher>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<WizardTemplateExecutor>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<PrepareSystemDriveInfoSource>()
            .As<IPrepareSystemDriveInfoSource>()
            .SingleInstance();

        builder.RegisterType<PrepareSystemTemplate>()
            .As<IWizardTemplate>()
            .SingleInstance();

        builder.RegisterType<QuickElevationTemplate>()
            .As<IWizardTemplate>()
            .SingleInstance();

        builder.RegisterType<ElevatedAppTemplate>()
            .As<IWizardTemplate>()
            .SingleInstance();

        builder.RegisterType<BrowserTemplate>()
            .As<IWizardTemplate>()
            .SingleInstance();

        builder.RegisterType<AiAgentTemplate>()
            .As<IWizardTemplate>()
            .SingleInstance();

        builder.RegisterType<CryptoWalletTemplate>()
            .As<IWizardTemplate>()
            .SingleInstance();

        builder.RegisterType<GamingAccountTemplate>()
            .As<IWizardTemplate>()
            .SingleInstance();

        builder.RegisterType<UntrustedAppTemplate>()
            .As<IWizardTemplate>()
            .SingleInstance();

        builder.RegisterType<WizardAccountSetupHelperFactory>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<WizardAccountPickerService>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AppEntryBuilder>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<WizardSessionSaverAdapter>()
            .As<IWizardSessionSaver>()
            .SingleInstance();

        builder.RegisterType<WizardLicenseChecker>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<WizardFolderGrantHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<WizardTemplateSetupBuilder>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<AiAgentFirewallOrchestrator>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GamingLogonBlockHelper>()
            .As<IGamingLogonBlockHelper>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<GamingExistingAccountPreparationService>()
            .AsSelf()
            .SingleInstance();

    }
}
