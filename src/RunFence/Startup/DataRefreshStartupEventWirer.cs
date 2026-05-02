using RunFence.Infrastructure;

namespace RunFence.Startup;

public class DataRefreshStartupEventWirer(
    IUiThreadInvoker uiThreadInvoker,
    IMainFormDataRefreshTarget mainForm,
    IConfigManagementEventSource configManagement,
    IEphemeralAccountChangeSource ephemeralAccountService,
    IEphemeralContainerChangeSource ephemeralContainerService,
    IApplicationDataChangeSource applicationDataChangeSource,
    IDataChangeNotifier dataChangeNotifier,
    IReencryptionWarningPresenter warningPresenter) : IStartupEventWirer
{
    public void WireEvents()
    {
        ephemeralAccountService.AccountsChanged += NotifyDataChangedOnUiThread;
        ephemeralContainerService.ContainersChanged += NotifyDataChangedOnUiThread;
        configManagement.ReencryptionWarning += warningPresenter.ShowWarning;
        configManagement.DataRefreshRequested += () => uiThreadInvoker.BeginInvoke(mainForm.SetData);
        configManagement.TrayUpdateRequested += () => uiThreadInvoker.BeginInvoke(mainForm.UpdateTray);
        applicationDataChangeSource.DataChanged += () => uiThreadInvoker.BeginInvoke(mainForm.HandleDataChanged);
    }

    private void NotifyDataChangedOnUiThread() =>
        uiThreadInvoker.BeginInvoke(dataChangeNotifier.NotifyDataChanged);
}
