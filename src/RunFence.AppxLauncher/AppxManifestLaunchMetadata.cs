namespace RunFence.AppxLauncher;

public readonly record struct AppxManifestLaunchMetadata(
    string PackageFamilyName,
    string AppUserModelId,
    string Command,
    string? Protocol,
    bool IsFullTrustApplication,
    bool SupportsMultipleInstances = false);
