namespace RunFence.Launching.Resolution;

public readonly record struct WindowsAppsPackageIdentity(
    string PackageFamilyName,
    string PackageFullName);
