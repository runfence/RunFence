namespace RunFence.Launching.Resolution;

public readonly record struct WindowsAppsPackagePath(
    string InstallRoot,
    string PackageName,
    Version Version,
    string Architecture,
    string PublisherId,
    string RelativeExecutablePath)
{
    public string PackageFamilyName => $"{PackageName}_{PublisherId}";
}
