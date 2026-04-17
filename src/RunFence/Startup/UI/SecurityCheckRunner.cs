using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Startup;
using RunFence.Startup.UI.Forms;

namespace RunFence.Startup.UI;

/// <summary>
/// Runs the startup security check asynchronously and shows the results dialog.
/// </summary>
public class SecurityCheckRunner(IModalCoordinator modalCoordinator, IStartupSecurityService startupSecurityService, ILoggingService log, FindingLocationHelper findingLocationHelper)
{
    /// <summary>
    /// Launches the security check. <paramref name="ownerControl"/> is the panel or control
    /// used as owner for the results dialog and for disabling during the operation.
    /// <paramref name="operationGuard"/> is acquired for the duration of the check.
    /// </summary>
    public async Task RunAsync(Control ownerControl, OperationGuard operationGuard)
    {
        operationGuard.Begin(ownerControl);
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var findings = await Task.Run(() => startupSecurityService.RunChecks(cts.Token));

            var isUacUnsafe = SidResolutionHelper.IsCurrentUserInteractive();

            if (findings.Count == 0)
            {
                // Only show "no issues" when there are truly no issues — skip if UAC warning will follow
                if (!isUacUnsafe)
                    MessageBox.Show("No security issues found.", "Security Check",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                using var dlg = new StartupSecurityDialog(findings, findingLocationHelper);
                modalCoordinator.ShowModal(dlg, ownerControl.FindForm());
            }

            if (isUacUnsafe)
                ShowUacWarning(ownerControl.FindForm());
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show("Security check timed out.", "Security Check",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            log.Error("Security check failed", ex);
            MessageBox.Show($"Security check failed: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            operationGuard.End(ownerControl);
        }
    }

    /// <summary>
    /// Shows the UAC same-account security warning dialog.
    /// Called when the elevated process is running under the same account as the interactive desktop user.
    /// </summary>
    public static void ShowUacWarning(IWin32Window? owner)
    {
        MessageBox.Show(owner,
            "RunFence is running elevated under the same account as the interactive desktop user.\n\n" +
            "By Microsoft's design, UAC elevation is not a security boundary — it is a convenience feature. " +
            "Any program running under this account can silently bypass UAC and gain administrator " +
            "privileges without any prompt.\n\n" +
            "For actual security isolation, create a separate dedicated administrator account, then remove " +
            "administrator rights from your current account and use it as a standard user for daily work. " +
            "When a UAC prompt appears, enter the credentials of that administrator account.",
            "UAC Security Warning",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}