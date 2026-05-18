using RunFence.Acl;
using RunFence.Launch;

namespace RunFence.Account;

/// <summary>
/// Opens system dialogs related to user account management.
/// Extracted from <see cref="WindowsAccountService"/> to separate the launch concern.
/// </summary>
public class SystemDialogLauncher(
    ILaunchFacade launchFacade,
    ILaunchFeedbackPresenter launchFeedbackPresenter)
    : ISystemDialogLauncher
{
    public void OpenUserAccountsDialog()
    {
        try
        {
            var lusrmgr = Path.Combine(Environment.SystemDirectory, "lusrmgr.msc");
            if (File.Exists(lusrmgr))
                using (var launch = launchFacade.LaunchFile("mmc.exe", AccountLaunchIdentity.CurrentAccountElevated, "lusrmgr.msc"))
                    launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext("User Accounts", LaunchFeedbackSource.InteractiveUi));
            else
                using (var launch = launchFacade.LaunchFile("control.exe", AccountLaunchIdentity.CurrentAccountElevated, "userpasswords2"))
                    launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext("User Accounts", LaunchFeedbackSource.InteractiveUi));
        }
        catch (OperationCanceledException)
        {
        }
        catch (GrantOperationException ex)
        {
            launchFeedbackPresenter.ShowGrantFailure(ex, new LaunchFeedbackContext("User Accounts", LaunchFeedbackSource.InteractiveUi));
        }
    }
}
