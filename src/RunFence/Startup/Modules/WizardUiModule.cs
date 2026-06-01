using Autofac;
using RunFence.Wizard;
using RunFence.Wizard.UI;
using RunFence.Wizard.UI.Forms;
using RunFence.Wizard.UI.Forms.Steps;

namespace RunFence.Startup.Modules;

public class WizardUiModule : Module
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<StandardAppWizardStepBuilder>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<AiAgentWizardStepBuilder>()
            .AsSelf()
            .InstancePerDependency();
        builder.RegisterType<GamingWizardStepBuilder>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<WizardCredentialCollector>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<WizardExecutionHandler>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<WizardNavigationHandler>()
            .AsSelf()
            .InstancePerDependency();

        builder.RegisterType<WizardDialog>()
            .AsSelf()
            .InstancePerDependency();
    }
}
