using RunFence.Core.Ipc;

namespace RunFence.Startup;

// Static by design: runs before DI container exists (pre-session startup)
public static class EarlyExitArgsHandler
{
    /// <summary>
    /// Handles early-exit CLI arguments (--load-apps, --unload-apps).
    /// Returns true if an early exit was triggered (Main should return immediately).
    /// </summary>
    public static bool HandleEarlyExitArgs(string[] args, IIpcClient ipcClient)
    {
        var (isEarlyExit, message) = args switch
        {
            ["--load-apps", _, ..] => (true, new IpcMessage { Command = IpcCommands.LoadApps, Arguments = args[1] }),
            ["--unload-apps", _, ..] => (true, new IpcMessage { Command = IpcCommands.UnloadApps, Arguments = args[1] }),
            _ => (false, null)
        };

        if (isEarlyExit && message != null)
        {
            try
            {
                ipcClient.SendMessage(message);
            }
            catch
            {
            } // server may not be running; exit regardless
        }

        return isEarlyExit;
    }
}
