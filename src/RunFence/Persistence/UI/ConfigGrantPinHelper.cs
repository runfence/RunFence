using RunFence.Acl.QuickAccess;
using RunFence.Core.Models;

namespace RunFence.Persistence.UI;

/// <summary>
/// Manages Quick Access pinning of granted folder paths around config load/unload operations.
/// Extracted from <see cref="ConfigManagementOrchestrator"/> to isolate
/// <see cref="IQuickAccessPinService"/> dependency.
/// </summary>
public class ConfigGrantPinHelper(IQuickAccessPinService quickAccessPinService)
{
    /// <summary>
    /// Pins all non-traverse Allow folder grants for all eligible accounts.
    /// Called after a successful config load.
    /// </summary>
    public void PinAllGrantedFolders() => quickAccessPinService.PinAllGrantedFolders();

    /// <summary>
    /// Snapshots all non-traverse Allow grant paths per account SID before an unload.
    /// The returned snapshot is passed to <see cref="UnpinRemovedGrantPaths"/> after unload.
    /// </summary>
    public static Dictionary<string, HashSet<string>> SnapshotAllowGrantPaths(AppDatabase database)
        => database.Accounts.ToDictionary(
            a => a.Sid,
            a => a.Grants
                .Where(g => g is { IsTraverseOnly: false, IsDeny: false })
                .Select(g => g.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Unpins folder paths that were present before an unload but are absent after.
    /// </summary>
    public void UnpinRemovedGrantPaths(
        AppDatabase database,
        Dictionary<string, HashSet<string>> snapshotBefore)
    {
        foreach (var (sid, pathsBefore) in snapshotBefore)
        {
            var currentPaths = database.GetAccount(sid)?.Grants
                .Where(g => g is { IsTraverseOnly: false, IsDeny: false })
                .Select(g => g.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            var unpinPaths = pathsBefore.Except(currentPaths, StringComparer.OrdinalIgnoreCase).ToList();
            if (unpinPaths.Count > 0)
                quickAccessPinService.UnpinFolders(sid, unpinPaths);
        }
    }
}
