namespace RunFence.AppxLauncher;

public readonly record struct AppxUriLaunchOptions(
    string PackageFamilyName,
    string Uri);
