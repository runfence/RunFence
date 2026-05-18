using RunFence.Acl;

namespace RunFence.Launch;

public interface ILaunchFeedbackPresenter
{
    void ShowMaintenanceWarning(LaunchExecutionResult result, LaunchFeedbackContext context);
    void ShowMaintenanceWarning(IReadOnlyList<string> maintenanceWarnings, LaunchFeedbackContext context);
    void ShowGrantFailure(GrantOperationException exception, LaunchFeedbackContext context);
    void ShowLaunchFailure(string message, Exception exception, LaunchFeedbackContext context);
}
