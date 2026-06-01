using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Launch;

namespace RunFence.Apps;

public sealed class VersionedPathRepairOptionsBuilder(IProfilePathResolver profilePathResolver)
{
    public VersionedPathRepairOptions ForAutomaticRepair(VersionedPathAutoRepairTrust trust)
        => CreateOptions([trust.TrustedRootPath]);

    public VersionedPathRepairOptions ForEditSuggestion(AppEntry app)
    {
        if (string.IsNullOrWhiteSpace(app.AccountSid) || !string.IsNullOrEmpty(app.AppContainerName))
            return VersionedPathRepairOptions.Empty;

        return CreateOptions([profilePathResolver.TryGetProfilePath(app.AccountSid)]);
    }

    private static VersionedPathRepairOptions CreateOptions(IEnumerable<string?> boundaryPaths)
    {
        var normalizedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var boundaryPath in boundaryPaths)
        {
            if (!RootedLocalPathNormalizer.TryNormalizeRootedLocalBoundaryPath(boundaryPath, out var normalizedPath))
                continue;

            normalizedPaths.Add(normalizedPath);
        }

        return normalizedPaths.Count == 0
            ? VersionedPathRepairOptions.Empty
            : new VersionedPathRepairOptions(normalizedPaths.ToArray());
    }
}
