using RunFence.Core;
using RunFence.Core.Ipc;

namespace RunFence.Launcher;

public static class LauncherCommandRouter
{
    public static LauncherCommandResult Route(string[] args, string commandLine)
    {
        if (args.Length < 1)
        {
            return new LauncherCommandResult(
                LauncherCommandKind.Invalid,
                null,
                null,
                null,
                LauncherFallbackAction.None,
                "Usage: RunFence.Launcher.exe <app-id> [arguments...]",
                1);
        }

        if (args is ["--open-folder", _, ..])
        {
            var rawTail = CommandLineHelper.SkipArgs(commandLine, 2);
            return new LauncherCommandResult(
                LauncherCommandKind.OpenFolder,
                rawTail,
                null,
                null,
                LauncherFallbackAction.None,
                null,
                0);
        }

        if (args[0].Equals("--unregister-folder-handler", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length != 1)
            {
                return new LauncherCommandResult(
                    LauncherCommandKind.Invalid,
                    null,
                    null,
                    null,
                    LauncherFallbackAction.None,
                    "Usage: RunFence.Launcher.exe --unregister-folder-handler",
                    1);
            }

            return new LauncherCommandResult(
                LauncherCommandKind.UnregisterFolderHandler,
                null,
                null,
                null,
                LauncherFallbackAction.None,
                null,
                0);
        }

        if (args is ["--resolve", _, ..])
        {
            var rawTail = CommandLineHelper.SkipArgs(commandLine, 3);
            return new LauncherCommandResult(
                LauncherCommandKind.HandleAssociation,
                rawTail,
                BuildIpcMessage(IpcCommands.HandleAssociation, association: args[1], arguments: rawTail),
                null,
                LauncherFallbackAction.None,
                null,
                0);
        }

        var appTail = CommandLineHelper.SkipArgs(commandLine, 2);
        return new LauncherCommandResult(
            LauncherCommandKind.LaunchApp,
            appTail,
            BuildIpcMessage(IpcCommands.Launch, appId: args[0], arguments: appTail),
            null,
            LauncherFallbackAction.None,
            null,
            0);
    }

    private static IpcMessage BuildIpcMessage(
        string command,
        string? appId = null,
        string? association = null,
        string? arguments = null)
    {
        var launcherDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var cwd = Environment.CurrentDirectory.TrimEnd('\\', '/');
        return new IpcMessage
        {
            Command = command,
            AppId = appId,
            Association = association,
            Arguments = arguments,
            WorkingDirectory = string.Equals(cwd, launcherDir, StringComparison.OrdinalIgnoreCase) ? null : Environment.CurrentDirectory
        };
    }
}
