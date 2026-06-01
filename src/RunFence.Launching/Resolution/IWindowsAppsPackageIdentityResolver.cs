namespace RunFence.Launching.Resolution;

public interface IWindowsAppsPackageIdentityResolver
{
    bool TryResolvePackageIdentity(string exePath, out WindowsAppsPackageIdentityResolution resolution);
}
