using RunFence.Core.Helpers;
using RunFence.Core.Ipc;

namespace RunFence.Launcher;

public static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        LauncherNative.AllowSetForegroundWindow(LauncherNative.ASFW_ANY);

        var notifier = new LauncherUserNotifier();
        var launcherIpcHelper = new LauncherIpcHelper(
            new LauncherIpcClient(),
            new LauncherGuiController(),
            new LauncherWaitDelay(),
            notifier);
        var launcherFallbackRegistry = new LauncherAssociationFallbackRegistry();
        var fallbackRestoreService = new AssociationFallbackRestoreService(launcherFallbackRegistry);
        var fallbackResolver = new LauncherAssociationFallbackCommandResolver(launcherFallbackRegistry);
        var processStarter = new LauncherProcessStarter();
        var fallbackService = new LauncherAssociationFallbackService(
            fallbackRestoreService,
            fallbackResolver,
            processStarter,
            notifier);
        var associationHandler = new AssociationHandler(launcherIpcHelper, fallbackService, notifier);
        var openFolderHandler = new OpenFolderHandler(launcherIpcHelper, processStarter, notifier);

        var command = LauncherCommandRouter.Route(args, Environment.CommandLine);
        if (command.CommandKind == LauncherCommandKind.Invalid)
        {
            notifier.ShowError(command.Warning ?? "Invalid command.");
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
                var response = launcherIpcHelper.SendWithAutoStart(command.IpcMessage!);
                if (response == null)
                    return 1;
                if (!response.Success)
                {
                    notifier.ShowError(response.ErrorMessage ?? "Unknown error.");
                    return 1;
                }

                return 0;
            }
            default:
                notifier.ShowError("Invalid command.");
                return 1;
        }
    }
}
