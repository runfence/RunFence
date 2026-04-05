using System.ComponentModel;
using RunFence.Core;
using RunFence.Launch;

namespace RunFence.RunAs;

public interface IRunAsLaunchErrorHandler
{
    void RunWithErrorHandling(Action launchAction, string filePath);
}

/// <summary>
/// Handles error reporting for RunAs launch failures.
/// Extracted from <see cref="RunAsAppEntryManager"/> so both it and <see cref="RunAsDirectLauncher"/> share
/// a single error-handling path without coupling <see cref="RunAsDirectLauncher"/> to <see cref="RunAsAppEntryManager"/>.
/// </summary>
public class RunAsLaunchErrorHandler(ILoggingService log) : IRunAsLaunchErrorHandler
{
    /// <summary>
    /// Invokes <paramref name="launchAction"/>, logs success, and shows a user-facing error message on failure.
    /// </summary>
    public void RunWithErrorHandling(Action launchAction, string filePath)
    {
        try
        {
            launchAction();
            log.Info($"RunAs launched: {filePath}");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ProcessLaunchNative.Win32ErrorLogonFailure)
        {
            MessageBox.Show("Stored credentials are incorrect. Please update the password.",
                "RunFence", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (Exception ex)
        {
            log.Error("RunAs launch failed", ex);
            MessageBox.Show($"Launch failed: {ex.Message}", "RunFence",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}