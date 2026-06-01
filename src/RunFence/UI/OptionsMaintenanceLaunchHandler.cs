using RunFence.Apps.UI;
using RunFence.Core;
using RunFence.Launch;

namespace RunFence.UI;

public class OptionsMaintenanceLaunchHandler(
    ILoggingService log,
    ILaunchFacade launchFacade,
    ILaunchFeedbackPresenter launchFeedbackPresenter,
    IMessageBoxService messageBoxService)
{
    public void OpenLogFile()
    {
        log.Info($"OpenLog: identity={System.Security.Principal.WindowsIdentity.GetCurrent().Name}, logPath={log.LogFilePath}, constantsPath={PathConstants.LogFilePath}");
        try
        {
            using var launch = launchFacade.LaunchFile(
                log.LogFilePath,
                AccountLaunchIdentity.CurrentAccountIsolated with
                {
                    AssociationResolutionPolicy = AssociationResolutionPolicy.AllowAccountRedirection
                });
            launchFeedbackPresenter.ShowMaintenanceWarning(
                launch,
                new LaunchFeedbackContext("The log viewer", LaunchFeedbackSource.InteractiveUi));
        }
        catch (Exception ex)
        {
            messageBoxService.Show(
                $"Failed to open log file: {ex.Message}",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
