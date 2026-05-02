using RunFence.Core;
using RunFence.Core.Ipc;

namespace RunFence.Ipc;

public class IpcMessageHandler(
    ILoggingService log,
    IpcConfigHandler configHandler,
    IpcLifecycleHandler lifecycleHandler,
    IpcLaunchHandler launchHandler,
    IpcOpenFolderHandler openFolderHandler,
    IpcAssociationHandler? associationHandler = null)
    : IIpcMessageHandler
{
    public IpcResponse HandleIpcMessage(IpcMessage message, IpcCallerContext context)
    {
        try
        {
            switch (message.Command)
            {
                case IpcCommands.Shutdown:
                    return lifecycleHandler.HandleShutdown(context.CallerIdentity, context.IsAdmin);

                case IpcCommands.Unlock:
                    return lifecycleHandler.HandleUnlockApp(context.CallerIdentity, context.CallerSid, context.IsAdmin);

                case IpcCommands.UnlockOperation:
                    return lifecycleHandler.HandleUnlockOperation(context.CallerIdentity, context.CallerSid, context.IsAdmin);

                case IpcCommands.LoadApps:
                    return configHandler.HandleLoadApps(context.CallerIdentity, context.IsAdmin, message);

                case IpcCommands.UnloadApps:
                    return configHandler.HandleUnloadApps(context.CallerIdentity, context.IsAdmin, message);

                case IpcCommands.OpenFolder:
                    return openFolderHandler.HandleOpenFolder(message, context.CallerIdentity, context.CallerSid);

                case IpcCommands.Launch:
                    return launchHandler.HandleLaunch(message, context);

                case IpcCommands.HandleAssociation:
                    if (associationHandler == null)
                        return new IpcResponse { Success = false, ErrorMessage = "Association handling not available." };
                    return associationHandler.HandleAssociation(message, context);

                default:
                    return new IpcResponse { Success = false, ErrorMessage = $"Unknown command: {message.Command}" };
            }
        }
        catch (Exception ex)
        {
            log.Error("IPC handler error", ex);
            return new IpcResponse { Success = false, ErrorMessage = "Internal error." };
        }
    }
}
