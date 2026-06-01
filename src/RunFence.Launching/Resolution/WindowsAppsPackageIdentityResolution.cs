namespace RunFence.Launching.Resolution;

public readonly record struct WindowsAppsPackageIdentityResolution(
    WindowsAppsPackageIdentity PackageIdentity,
    string PackageExecutablePath);
