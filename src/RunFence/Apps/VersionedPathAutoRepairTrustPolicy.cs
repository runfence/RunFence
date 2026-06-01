using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Apps;

public sealed class VersionedPathAutoRepairTrustPolicy(
    IProgramFilesPathProvider programFilesPathProvider,
    IProfilePathResolver profilePathResolver)
{
    public bool TryCreateAutoRepairTrust(AppEntry app, out VersionedPathAutoRepairTrust trust)
    {
        trust = default;

        if (!RootedLocalPathNormalizer.TryNormalizeRootedLocalBoundaryPath(app.ExePath, out var normalizedAppPath))
            return false;

        foreach (var programFilesRoot in programFilesPathProvider.GetProgramFilesRoots())
        {
            if (!RootedLocalPathNormalizer.TryNormalizeRootedLocalBoundaryPath(programFilesRoot, out var normalizedRoot))
                continue;

            if (IsSameOrDescendant(normalizedAppPath, normalizedRoot))
            {
                trust = new VersionedPathAutoRepairTrust(normalizedRoot);
                return true;
            }
        }

        if (!string.IsNullOrEmpty(app.AppContainerName) || string.IsNullOrEmpty(app.AccountSid))
            return false;

        var profileRoot = profilePathResolver.TryGetProfilePath(app.AccountSid);
        if (!RootedLocalPathNormalizer.TryNormalizeRootedLocalBoundaryPath(profileRoot, out var normalizedProfileRoot))
            return false;

        if (!IsSameOrDescendant(normalizedAppPath, normalizedProfileRoot))
            return false;

        trust = new VersionedPathAutoRepairTrust(normalizedProfileRoot);
        return true;
    }

    private static bool IsSameOrDescendant(string path, string root)
    {
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
            return true;

        string relativePath;
        try
        {
            relativePath = Path.GetRelativePath(root, path);
        }
        catch
        {
            return false;
        }

        return relativePath.Length > 0
               && !relativePath.StartsWith("..", StringComparison.Ordinal)
               && !Path.IsPathRooted(relativePath);
    }
}
