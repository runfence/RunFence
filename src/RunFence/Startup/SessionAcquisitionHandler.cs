using System.Diagnostics;
using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Launch;
using RunFence.Persistence;
using RunFence.Startup.UI;

namespace RunFence.Startup;

public class SessionAcquisitionHandler(
    IStartupUI ui,
    IConfigPaths configPaths,
    IIpcClient ipcClient,
    IRunningInstanceSidProvider runningInstanceSidProvider)
{
    /// <summary>
    /// Acquires the single-instance mutex or performs session takeover.
    /// Returns true if the mutex was acquired (Main should continue).
    /// </summary>
    public bool AcquireMutexOrTakeover(
        ISingleInstanceService singleInstance,
        bool isBackground,
        ILoggingService log)
    {
        if (singleInstance.TryAcquire())
            return true;

        // always try to show/unlock existing instance regardless of --unlock
        if (UnlockExistingInstance(log))
            return false;

        var isFirstRun = !File.Exists(configPaths.CredentialsFilePath);

        if (!ui.ConfirmTakeover(isFirstRun, isBackground))
            return false;

        try
        {
            ipcClient.SendMessage(new IpcMessage { Command = IpcCommands.Shutdown });
        }
        catch
        {
        } // best effort; other instance may already be gone

        bool acquired = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            // Thread.Sleep is intentional here: this runs before Application.Run, so there is no
            // message loop to pump. Async/await is not viable at this point in the startup sequence.
            // We wait up to 3 seconds (3 x 1s) for the prior instance to release the mutex after
            // receiving the Shutdown IPC message.
            Thread.Sleep(1000);
            if (singleInstance.TryAcquire())
            {
                acquired = true;
                break;
            }
        }

        if (!acquired)
        {
            ui.ShowError("Failed to take over. The other instance may still be running.", "Takeover Failed");
            return false;
        }

        log.Info("Session takeover successful");
        return true;
    }

    public bool UnlockExistingInstance(ILoggingService log, string command = IpcCommands.Unlock)
    {
        var info = runningInstanceSidProvider.GetRunningInstanceInfo();
        if (info != null &&
            string.Equals(info.Sid, SidResolutionHelper.GetCurrentUserSid(), StringComparison.OrdinalIgnoreCase) &&
            info.SessionId == Process.GetCurrentProcess().SessionId)
        {
            try
            {
                ProcessLaunchNative.AllowSetForegroundWindow(ProcessLaunchNative.ASFW_ANY);

                ipcClient.SendMessage(new IpcMessage { Command = command });
                log.Info($"Sent unlock to running instance (same account, command={command})");
                return true;
            }
            catch (Exception ex)
            {
                log.Warn("Failed to send unlock to running instance; falling through to takeover.\n" + ex);
            }
        }

        return false;
    }
}
