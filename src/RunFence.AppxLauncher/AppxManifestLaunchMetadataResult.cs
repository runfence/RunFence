namespace RunFence.AppxLauncher;

public readonly record struct AppxManifestLaunchMetadataResult(
    bool Success,
    AppxManifestLaunchMetadata Metadata,
    AppxLaunchResult Error)
{
    public static AppxManifestLaunchMetadataResult Succeeded(AppxManifestLaunchMetadata metadata) =>
        new(true, metadata, default);

    public static AppxManifestLaunchMetadataResult Failed(AppxLaunchResult error) =>
        new(false, default, error);
}
