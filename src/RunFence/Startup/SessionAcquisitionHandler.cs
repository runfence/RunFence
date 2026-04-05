using RunFence.Core;
using RunFence.Core.Ipc;
using RunFence.Startup.UI;

namespace RunFence.Startup;

public class SessionAcquisitionHandler(IStartupUI ui)
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

        var isFirstRun = !File.Exists(Path.Combine(Constants.LocalAppDataDir, "credentials.dat"));

        if (!ui.ConfirmTakeover(isFirstRun, isBackground))
            return false;

        if (isBackground)
            log.Info("Silent takeover requested via --background mode");

        try
        {
            IpcClient.SendMessage(new IpcMessage { Command = IpcCommands.Shutdown });
        }
        catch
        {
        } // best effort; other instance may already be gone

        bool acquired = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
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
}