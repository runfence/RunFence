using RunFence.Account.UI.Forms;
using RunFence.Apps.UI.Forms;
using RunFence.Groups.UI.Forms;
using RunFence.Licensing.UI.Forms;
using RunFence.Persistence.UI;

namespace RunFence.UI.Forms;

public interface IMainFormContentView : IMainFormVisibility
{
    Control FormControl { get; }
    event EventHandler ActivationRefreshRequested;
    void AttachApplicationsPanel(ApplicationsPanel panel);
    void AttachAccountsPanel(AccountsPanel panel);
    void AttachGroupsPanel(GroupsPanel panel);
    void AttachOptionsPanel(OptionsPanel panel);
    void AttachAboutPanel(AboutPanel panel);
    void SelectAccountsTab();
    void ScheduleAvailabilityCheck();
    void QueueSelectedTabRefresh();
    void NavigateToAccount(string accountSid);
    void NavigateToApp(string appId);
    void OpenAddDialogForAccount(string accountSid);
    void HandleOptionsSettingsChanged();
    void RequestCleanup();
    void RequestMigrationExit();
    void RequestConfigLoad(string path);
    void RequestConfigUnload(string path);
    void RequestEnforcement();
}
