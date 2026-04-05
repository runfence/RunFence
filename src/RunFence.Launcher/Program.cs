using RunFence.Core;
using RunFence.Core.Ipc;

namespace RunFence.Launcher;

public static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            LauncherIpcHelper.ShowError("Usage: RunFence.Launcher.exe <app-id> [arguments...]");
            return 1;
        }

        // RunFence.Launcher.exe is always created by a user action (shell association, shortcut, etc.)
        // so Windows grants it foreground activation rights. Pass those rights to any process so that
        // the app RunFence.exe ultimately launches can bring its window to the foreground.
        LauncherNative.AllowSetForegroundWindow(LauncherNative.ASFW_ANY);

        switch (args)
        {
            case ["--open-folder", _, ..]:
            {
                // MSVCRT/CommandLineToArgvW bug: "%V" for paths ending in "\" (e.g. "O:\") is misquoted
                // as "O:\" where \" is treated as an escaped quote, so the path arrives with a spurious
                // trailing '"'. Since '"' is never valid in Windows paths, strip it. Drive roots (e.g.
                // "O:") also lose their trailing '\' to the escape, so restore it.
                var folderPath = args[1].TrimEnd('"');
                if (folderPath is [_, ':'])
                    folderPath += '\\';
                return OpenFolderHandler.Handle(folderPath);
            }
            // Handler association: --resolve <association> <path/URL>
            case ["--resolve", _, ..]:
            {
                // Extract everything after --resolve <association> verbatim to preserve quoting
                string? rawArguments = args.Length > 2 ? CommandLineHelper.SkipArgs(Environment.CommandLine, 3) : null;
                return AssociationHandler.Handle(args[1], rawArguments);
            }
        }

        var appId = args[0];

        // Extract the extra args verbatim from the raw command line by skipping the first
        // two tokens (exe path + appId), preserving the original quoting exactly.
        // For example: launcher.exe appid a b "c d" "e"  →  a b "c d" "e"
        // Using Environment.CommandLine instead of re-joining args[] avoids any
        // parse/re-quote round-trip that would lose or alter the original quoting.
        string? extraArgs = args.Length > 1 ? CommandLineHelper.SkipArgs(Environment.CommandLine, 2) : null;

        // Send launch command; omit WorkingDirectory when it matches the launcher's own directory
        // so the app entry's configured default working directory is used instead.
        var launcherDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        var cwd = Environment.CurrentDirectory.TrimEnd('\\', '/');
        var message = new IpcMessage
        {
            Command = IpcCommands.Launch,
            AppId = appId,
            Arguments = extraArgs,
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