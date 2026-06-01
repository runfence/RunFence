namespace RunFence.AppxLauncher;

public readonly record struct AppxLauncherStartupOptions(
    string LogFilePath,
    string AppxExecutablePath,
    string Arguments);
