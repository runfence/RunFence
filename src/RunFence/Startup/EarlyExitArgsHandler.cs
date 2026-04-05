using RunFence.Core.Ipc;

namespace RunFence.Startup;

public static class EarlyExitArgsHandler
{
    /// <summary>
    /// Handles early-exit CLI arguments (--unlock, --load-apps, --unload-apps).
    /// Returns true if an early exit was triggered (Main should return immediately).
    /// </summary>
    public static bool HandleEarlyExitArgs(string[] args)
    {
        switch (args)
        {
            case ["--unlock", ..]:
                try
                {
                    IpcClient.SendMessage(new IpcMessage { Command = IpcCommands.Unlock });
                }
                catch
                {
                } // server may not be running; exit regardless

                return true;
            case ["--load-apps", _, ..]:
                try
                {
                    IpcClient.SendMessage(new IpcMessage { Command = IpcCommands.LoadApps, Arguments = args[1] });
                }
                catch
                {
                } // server may not be running; exit regardless

                return true;
            case ["--unload-apps", _, ..]:
                try
                {
                    IpcClient.SendMessage(new IpcMessage { Command = IpcCommands.UnloadApps, Arguments = args[1] });
                }
                catch
                {
                } // server may not be running; exit regardless

                return true;
            default:
                return false;
        }
    }
}