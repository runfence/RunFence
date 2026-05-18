using RunFence.Core.Ipc;

namespace RunFence.Launcher;

/// <summary>
/// Handles the <c>--resolve &lt;association&gt; &lt;path/URL&gt;</c> command-line argument,
/// sending a <see cref="IpcCommands.HandleAssociation"/> IPC message to RunFence.
/// </summary>
public sealed class AssociationHandler(
    ILauncherIpcCommandSender commandSender,
    ILauncherAssociationFallbackService fallbackService,
    ILauncherUserNotifier notifier)
{
    public int Handle(string association, string? rawArguments)
    {
        var message = new IpcMessage
        {
            Command = IpcCommands.HandleAssociation,
            Association = association,
            Arguments = rawArguments,
            WorkingDirectory = GetWorkingDirectory()
        };

        var response = commandSender.SendWithAutoStart(message);
        if (response == null)
            return 1;

        return HandleResponse(association, rawArguments, response);
    }

    public int HandleResponse(string association, string? rawArguments, IpcResponse response)
    {
        if (response.Success)
        {
            if (!string.IsNullOrWhiteSpace(response.WarningMessage))
                notifier.ShowWarning(response.WarningMessage);
            return 0;
        }

        switch (response.ErrorCode)
        {
            case IpcErrorCode.AccessDenied:
            case IpcErrorCode.UnknownAssociation:
            case IpcErrorCode.AppNotFound:
                return fallbackService.CleanupAndLaunchFallback(association, rawArguments);
            case IpcErrorCode.PathPrefixMismatch:
                return fallbackService.LaunchFallback(association, rawArguments);
            default:
                notifier.ShowError(response.ErrorMessage ?? "Unknown error.");
                return 1;
        }
    }

    private static string? GetWorkingDirectory()
    {
        var launcherDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var cwd = Environment.CurrentDirectory.TrimEnd('\\', '/');
        return string.Equals(cwd, launcherDir, StringComparison.OrdinalIgnoreCase) ? null : Environment.CurrentDirectory;
    }
}
