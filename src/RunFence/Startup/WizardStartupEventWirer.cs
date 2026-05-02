using RunFence.Infrastructure;

namespace RunFence.Startup;

public class WizardStartupEventWirer(
    IWizardRequestSource wizardRequestSource,
    IWizardLauncher wizardLauncher,
    IMainFormDataRefreshTarget mainForm) : IStartupEventWirer
{
    public void WireEvents()
    {
        wizardRequestSource.WizardRequested += async owner => await wizardLauncher.OpenWizardAsync(owner);
        wizardRequestSource.WizardButtonEnabled = true;
        wizardLauncher.WizardCompleted += mainForm.HandleDataChanged;
    }
}
