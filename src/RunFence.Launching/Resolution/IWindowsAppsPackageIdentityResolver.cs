namespace RunFence.Launching.Resolution;

public interface IWindowsAppsPackageIdentityResolver
{
    bool TryResolvePackageFamilyName(string exePath, out string packageFamilyName);
}
