namespace RunFence.Launch;

public readonly record struct WindowsAppsActivationTarget(
    ProcessLaunchTarget HelperTarget,
    string ResultDirectoryPath,
    string ResultFilePath,
    string AppxExecutablePath,
    string Arguments);
