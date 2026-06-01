namespace RunFence.AppxLauncher;

public interface IDesktopAppxActivationLauncher
{
    AppxLaunchResult Launch(AppxManifestLaunchMetadata metadata, string arguments);
}
