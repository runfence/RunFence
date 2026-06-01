using RunFence.Core;

namespace RunFence.AppxLauncher;

public static class AppxLauncherArgumentParser
{
    public static bool TryParse(string[] args, string rawCommandLine, out AppxLauncherStartupOptions options, out string error)
    {
        if (args.Length < 2)
        {
            options = default;
            error = "Expected at least 2 parsed arguments: <logFilePath> <appxExecutablePath> <argumentsTail...>.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(args[0]))
        {
            options = default;
            error = "Log file path must not be empty.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(args[1]))
        {
            options = default;
            error = "AppX executable path must not be empty.";
            return false;
        }

        var arguments = CommandLineHelper.SliceVerbatimTail(rawCommandLine, 3) ?? string.Empty;
        options = new AppxLauncherStartupOptions(args[0], args[1], arguments);
        error = string.Empty;
        return true;
    }
}
