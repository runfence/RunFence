namespace RunFence.Apps.Shortcuts;

public sealed record RegisteredAppxPackage(
    string PackageFamilyName,
    string PackageFullName,
    string InstallLocation);
