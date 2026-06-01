namespace RunFence.Launching.Resolution;

public sealed class WindowsAppsPackageIdentityResolver(
    IWindowsAppsAliasPathResolver aliasPathResolver,
    IAppExecLinkReader appExecLinkReader)
    : IWindowsAppsPackageIdentityResolver
{
    public bool TryResolvePackageIdentity(string exePath, out WindowsAppsPackageIdentityResolution resolution)
        => TryResolvePackageIdentity(
            exePath,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            out resolution);

    private bool TryResolvePackageIdentity(
        string exePath,
        HashSet<string> visitedPaths,
        out WindowsAppsPackageIdentityResolution resolution)
    {
        string normalizedPath;
        try
        {
            normalizedPath = Path.GetFullPath(exePath);
        }
        catch
        {
            resolution = default;
            return false;
        }

        if (!visitedPaths.Add(normalizedPath))
        {
            resolution = default;
            return false;
        }

        if (WindowsAppsPackagePathParser.TryParsePackagePath(normalizedPath, out var directPackagePath))
        {
            resolution = new WindowsAppsPackageIdentityResolution(
                new WindowsAppsPackageIdentity(
                    directPackagePath.PackageFamilyName,
                    directPackagePath.PackageFullName),
                normalizedPath);
            return true;
        }

        if (aliasPathResolver.IsWindowsAppsAliasPath(normalizedPath)
            && appExecLinkReader.TryReadStrings(normalizedPath, out var strings)
            && TryResolvePackageIdentityFromAlias(strings, visitedPaths, out resolution))
        {
            return true;
        }

        resolution = default;
        return false;
    }

    private bool TryResolvePackageIdentityFromAlias(
        IEnumerable<string> strings,
        HashSet<string> visitedPaths,
        out WindowsAppsPackageIdentityResolution resolution)
    {
        foreach (var value in strings)
        {
            if (TryResolvePackageIdentity(value, visitedPaths, out resolution))
            {
                return true;
            }
        }

        resolution = default;
        return false;
    }
}
