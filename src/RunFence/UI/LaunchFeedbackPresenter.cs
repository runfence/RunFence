using RunFence.Account.UI;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Launch;

namespace RunFence.UI;

public sealed class LaunchFeedbackPresenter(
    ILoggingService log,
    IAccountMessageBoxService messageBoxService,
    ITrayWarningSink trayWarningSink,
    IClock clock)
    : ILaunchFeedbackPresenter
{
    private static readonly TimeSpan TrayNotificationWindow = TimeSpan.FromMinutes(5);
    private const int MaxTrayNotificationsPerWindow = 5;

    private readonly SlidingWindowNotificationGate _trayWarningGate =
        new(clock, TrayNotificationWindow, MaxTrayNotificationsPerWindow);

    public void ShowMaintenanceWarning(LaunchExecutionResult result, LaunchFeedbackContext context)
        => ShowMaintenanceWarning(result.MaintenanceWarnings, context);

    public void ShowMaintenanceWarning(IReadOnlyList<string> maintenanceWarnings, LaunchFeedbackContext context)
    {
        if (maintenanceWarnings.Count == 0)
            return;

        var warning = LaunchExecutionWarningFormatter.Format(context.StartedItem, maintenanceWarnings);
        if (warning == null)
            return;

        log.Warn(warning);
        var dialogOwner = HasVisibleOwner(context.Owner)
            ? context.Owner
            : null;

        if (ShouldShowDialog(context))
        {
            messageBoxService.Show(
                dialogOwner,
                warning,
                context.WarningCaption,
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        ShowTrayWarning($"{GetSummaryName(context)} started with warnings.");
    }

    public void ShowGrantFailure(GrantOperationException exception, LaunchFeedbackContext context)
    {
        var message = context.UseRunAsGrantFailureWording
            ? FormatRunAsGrantFailure(exception, context.GrantFailureSubject ?? context.StartedItem)
            : GrantApplyFailureFormatter.Format(exception.Step, exception.Path, exception.ConfigPath, exception.Cause);

        ShowFailure(message, exception, context);
    }

    public void ShowLaunchFailure(string message, Exception exception, LaunchFeedbackContext context)
    {
        ShowFailure(message, exception, context);
    }

    private void ShowFailure(string message, Exception exception, LaunchFeedbackContext context)
    {
        log.Error(message, exception);
        var dialogOwner = HasVisibleOwner(context.Owner)
            ? context.Owner
            : null;

        if (ShouldShowDialog(context))
        {
            messageBoxService.Show(
                dialogOwner,
                message,
                context.FailureCaption,
                MessageBoxButtons.OK,
                context.FailureIcon);
            return;
        }

        ShowTrayWarning($"Launch failed: {GetSummaryName(context)}");
    }

    private bool ShouldShowDialog(LaunchFeedbackContext context)
        => context.Source == LaunchFeedbackSource.InteractiveUi;

    private static bool HasVisibleOwner(IWin32Window? owner)
    {
        if (owner == null)
            return false;

        if (owner is Control control)
            return !control.IsDisposed && control.IsHandleCreated && control.Visible;

        return owner.Handle != IntPtr.Zero;
    }

    private void ShowTrayWarning(string summary)
    {
        if (_trayWarningGate.TryAcquire())
            trayWarningSink.ShowWarning(summary);
    }

    private static string GetSummaryName(LaunchFeedbackContext context)
        => context.SummaryName ?? context.StartedItem;

    private static string FormatRunAsGrantFailure(GrantOperationException exception, string filePath)
    {
        var detail = GrantApplyFailureFormatter.Format(exception.Step, exception.Path, exception.ConfigPath, exception.Cause);
        return GrantApplyFailureFormatter.IsSaveFailureStep(exception.Step)
            ? $"RunFence could not save the access grant required to launch '{filePath}': {detail}"
            : $"RunFence saved the access grant required to launch '{filePath}', but applying filesystem access failed: {detail}";
    }
}
