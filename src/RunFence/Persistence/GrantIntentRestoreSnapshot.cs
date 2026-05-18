using RunFence.Core.Models;

namespace RunFence.Persistence;

public sealed class GrantIntentRestoreSnapshot
{
    public GrantIntentRestoreSnapshot(
        GrantedPathEntry? runtimeEntry,
        IReadOnlyList<GrantIntentRestoreLocation> locations)
    {
        RuntimeEntry = runtimeEntry?.Clone();
        Locations = locations
            .Select(location => new GrantIntentRestoreLocation(location.ConfigPath, location.Entry))
            .ToList();
    }

    public GrantedPathEntry? RuntimeEntry { get; }

    public IReadOnlyList<GrantIntentRestoreLocation> Locations { get; }
}
