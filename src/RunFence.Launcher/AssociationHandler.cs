using RunFence.Core.Ipc;

namespace RunFence.Launcher;

/// <summary>
/// Handles the <c>--resolve &lt;association&gt; &lt;path/URL&gt;</c> command-line argument,
/// sending a <see cref="IpcCommands.HandleAssociation"/> IPC message to RunFence.
/// </summary>
public static class AssociationHandler
{
    /// <summary>
    /// Sends a HandleAssociation IPC message and returns 0 on success, 1 on failure.
    /// </summary>
    /// <param name="association">The association key (e.g., "http", ".pdf").</param>
    /// <param name="rawArguments">The raw path/URL extracted verbatim from the command line.</param>
    public static int Handle(string association, string? rawArguments)
    {
        // Omit WorkingDirectory when it matches the launcher's own directory so the app
        // entry's configured default working directory is used instead.
        var launcherDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var cwd = Environment.CurrentDirectory.TrimEnd('\\', '/');
        var message = new IpcMessage
        {
            Command = IpcCommands.HandleAssociation,
            Association = association,
            Arguments = rawArguments,
            WorkingDirectory = string.Equals(cwd, launcherDir, StringComparison.OrdinalIgnoreCase) ? null : Environment.CurrentDirectory
        };

        var response = LauncherIpcHelper.SendWithAutoStart(message);
        if (response == null)
            return 1;

        if (!response.Success)
        {
            LauncherIpcHelper.ShowError(response.ErrorMessage ?? "Unknown error.");
            return 1;
        }

        return 0;
    }
}