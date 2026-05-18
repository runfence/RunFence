using System.ComponentModel;
using RunFence.Acl;
using RunFence.Core;
using RunFence.Launch;

namespace RunFence.RunAs;

/// <summary>
/// Handles error reporting for RunAs launch failures.
/// Extracted from <see cref="RunAsAppEntryManager"/> so both it and <see cref="RunAsDirectLauncher"/> share
/// a single error-handling path without coupling <see cref="RunAsDirectLauncher"/> to <see cref="RunAsAppEntryManager"/>.
/// </summary>
public class RunAsLaunchErrorHandler(
    ILaunchFeedbackPresenter launchFeedbackPresenter,
    ILoggingService log)
    : IRunAsLaunchErrorHandler
{
    /// <summary>
    /// Invokes <paramref name="launchAction"/>, logs success, and shows a user-facing error message on failure.
    /// </summary>
    public void RunWithErrorHandling(Func<LaunchExecutionResult> launchAction, string filePath)
    {
        try
        {
            using var launch = launchAction();
            launchFeedbackPresenter.ShowMaintenanceWarning(launch, new LaunchFeedbackContext("The application", LaunchFeedbackSource.InteractiveUi)
            {
                SummaryName = Path.GetFileName(filePath)
            });
            log.Info($"RunAs launched: {filePath}");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorLogonFailure)
        {
            MessageBox.Show("Stored credentials are incorrect. Please update the password.",
                "RunFence", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (OperationCanceledException)
        {
        }
        catch (GrantOperationException ex)
        {
            launchFeedbackPresenter.ShowGrantFailure(ex, new LaunchFeedbackContext("The application", LaunchFeedbackSource.InteractiveUi)
            {
                SummaryName = Path.GetFileName(filePath),
                GrantFailureSubject = filePath,
                UseRunAsGrantFailureWording = true,
                FailureCaption = "RunFence"
            });
        }
        catch (Exception ex)
        {
            log.Error("RunAs launch failed", ex);
            MessageBox.Show($"Launch failed: {ex.Message}", "RunFence",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
