using RunFence.Core.Helpers;
using RunFence.Core.Ipc;

namespace RunFence.Launcher;

public static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        LauncherNative.AllowSetForegroundWindow(LauncherNative.ASFW_ANY);

        var launcherIpcHelper = new LauncherIpcHelper(
            new LauncherIpcClient(),
            new LauncherGuiController(),
            new LauncherWaitDelay(),
            new LauncherUserNotifier());
        var commandSender = new LauncherIpcCommandSender(launcherIpcHelper);
        var notifier = new LauncherUserNotifier();
        var launcherFallbackRegistry = new LauncherAssociationFallbackRegistry();
        var fallbackRestoreService = new AssociationFallbackRestoreService(launcherFallbackRegistry);
        var fallbackResolver = new LauncherAssociationFallbackCommandResolver(launcherFallbackRegistry);
        var processStarter = new LauncherProcessStarter();
        var fallbackService = new LauncherAssociationFallbackService(
            fallbackRestoreService,
            fallbackResolver,
            processStarter,
            notifier);
        var associationHandler = new AssociationHandler(commandSender, fallbackService, notifier);
        var openFolderHandler = new OpenFolderHandler(commandSender, processStarter);

        var command = LauncherCommandRouter.Route(args, Environment.CommandLine);
        if (command.CommandKind == LauncherCommandKind.Invalid)
        {
            LauncherIpcHelper.ShowError(command.Warning ?? "Invalid command.");
            return command.ExitCode;
        }

        switch (command.CommandKind)
        {
            case LauncherCommandKind.OpenFolder:
            {
                var folderPath = command.RawTail ?? string.Empty;
                if (folderPath is ['"', _, ..] && folderPath[^1] == '"')
                    folderPath = folderPath[1..^1];
                folderPath = folderPath.TrimEnd('"');
                if (folderPath is [_, ':'])
                    folderPath += '\\';
                return openFolderHandler.Handle(folderPath);
            }
            case LauncherCommandKind.UnregisterFolderHandler:
            {
                return openFolderHandler.Unregister();
            }
            case LauncherCommandKind.HandleAssociation:
            {
                return associationHandler.Handle(command.IpcMessage!.Association!, command.RawTail);
            }
            case LauncherCommandKind.LaunchApp:
            {
                var response = commandSender.SendWithAutoStart(command.IpcMessage!);
                if (response == null)
                    return 1;
                if (!response.Success)
                {
                    LauncherIpcHelper.ShowError(response.ErrorMessage ?? "Unknown error.");
                    return 1;
                }

                return 0;
            }
            default:
                LauncherIpcHelper.ShowError("Invalid command.");
                return 1;
        }
    }
}
