using System.Diagnostics;
using RunFence.Core;
using RunFence.Core.Ipc;

namespace RunFence.Launcher;

/// <summary>
/// Shared helper that ensures the RunFence GUI is running, waits for the IPC pipe to be ready,
/// sends a message, and returns the response. Shows an error dialog on failure.
/// Eliminates the triplicated GUI-check + start + wait + send flow.
/// </summary>
public class LauncherIpcHelper(
    ILauncherIpcClient ipcClient,
    ILauncherGuiController guiController,
    ILauncherWaitDelay waitDelay)
{
    /// <summary>
    /// Ensures the RunFence GUI is running, waits for IPC readiness, sends
    /// <paramref name="message"/>, and returns the response.
    /// Returns null if communication failed; an error dialog has already been shown in that case.
    /// </summary>
    public IpcResponse? SendWithAutoStart(IpcMessage message)
    {
        bool guiRunning = guiController.IsGuiRunning();

        if (guiRunning)
        {
            if (!WaitForServerWhile(guiController.IsGuiRunning))
            {
                guiRunning = guiController.IsGuiRunning();
                if (guiRunning)
                {
                    ShowError("RunFence is not responding. Please try again or start it manually.");
                    return null;
                }
            }
        }

        if (!guiRunning)
        {
            if (!guiController.StartGui(IsRunAsStartupRequest(message)))
            {
                ShowError("Failed to start RunFence.");
                return null;
            }

            if (!WaitForServerWhile(() => true))
            {
                ShowError("RunFence is not responding. Please try again or start it manually.");
                return null;
            }
        }

        var response = ipcClient.SendMessage(message);
        if (response == null)
        {
            ShowError("Failed to communicate with RunFence.");
            return null;
        }

        return response;
    }

    private bool WaitForServerWhile(Func<bool> shouldKeepWaiting)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < IpcConstants.LauncherTimeoutMs)
        {
            if (ipcClient.PingServer())
                return true;

            if (!shouldKeepWaiting())
                return false;

            waitDelay.Sleep(500);
        }

        return false;
    }

    private static bool IsRunAsStartupRequest(IpcMessage message) =>
        message.Command == IpcCommands.Launch
        && message.AppId?.IndexOfAny(['\\', '/']) >= 0;

    public static void ShowError(string message)
    {
        MessageBox.Show(message, "RunFence Launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
