namespace RunFence.AppxLauncher;

public interface IAppxManifestLaunchMetadataResolver
{
    AppxManifestLaunchMetadataResult Resolve(string appxExecutablePath, string arguments);
}
