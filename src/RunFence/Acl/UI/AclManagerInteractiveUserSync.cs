using System.Security.AccessControl;
using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

/// <summary>
/// Mirrors all grant/traverse changes to the interactive desktop user when the ACL
/// Manager is managing an AppContainer SID. AppContainer tokens require dual access
/// checks: both the container SID and the user SID must independently have access to
/// every path. This handler keeps both in sync whenever the ACL Manager applies changes.
/// </summary>
public class AclManagerInteractiveUserSync
{
    private readonly IGrantedPathAclService _aclService;
    private readonly IAclPermissionService _aclPermission;
    private readonly IUserTraverseService _userTraverseService;
    private readonly ILoggingService _log;
    private readonly IDatabaseProvider _databaseProvider;
    private readonly ISessionSaver _sessionSaver;
    private string _interactiveUserSid = null!;

    public AclManagerInteractiveUserSync(
        IGrantedPathAclService aclService,
        IAclPermissionService aclPermission,
        ILoggingService log,
        IUserTraverseService userTraverseService,
        IDatabaseProvider databaseProvider,
        ISessionSaver sessionSaver)
    {
        _aclService = aclService;
        _aclPermission = aclPermission;
        _log = log;
        _userTraverseService = userTraverseService;
        _databaseProvider = databaseProvider;
        _sessionSaver = sessionSaver;
    }

    public void Initialize(string interactiveUserSid)
    {
        _interactiveUserSid = interactiveUserSid;
    }

    /// <summary>
    /// Executes the full interactive-user sync pipeline (phases A, B, C).
    /// </summary>
    public async Task SyncAsync(
        List<GrantedPathEntry> pendingAdds,
        List<GrantedPathEntry> pendingRemoves,
        List<GrantedPathEntry> pendingModifications,
        List<GrantedPathEntry> pendingTraverseAdds,
        List<GrantedPathEntry> pendingTraverseRemoves,
        List<GrantedPathEntry> pendingTraverseFixes,
        List<GrantedPathEntry> pendingUntrackGrants,
        List<GrantedPathEntry> pendingUntrackTraverse,
        HashSet<(string Path, bool IsDeny)> successfulRemoves,
        HashSet<string> successfulTraverseRemoves,
        List<(string Path, string Error)> errors)
    {
        var iuSid = _interactiveUserSid;
        var database = _databaseProvider.GetDatabase();

        await SyncPhaseAAsync(iuSid, database, pendingRemoves, pendingTraverseRemoves,
            successfulRemoves, successfulTraverseRemoves, errors);

        var alreadyCoveredPaths = await ComputeAlreadyCoveredPathsAsync(
            iuSid, pendingAdds, pendingModifications);

        SyncPhaseB(iuSid, database, pendingAdds, pendingModifications,
            pendingTraverseAdds, pendingUntrackGrants, pendingUntrackTraverse,
            successfulRemoves, successfulTraverseRemoves, alreadyCoveredPaths, errors);

        await SyncPhaseCAsync(iuSid, database, pendingAdds, pendingModifications,
            pendingTraverseAdds, pendingTraverseFixes, alreadyCoveredPaths, errors);

        if (pendingTraverseFixes.Count > 0)
        {
            try
            {
                _sessionSaver.SaveConfig();
            }
            catch (Exception ex)
            {
                _log.Error("Sync: failed to persist interactive user traverse fix paths", ex);
                errors.Add(("(database - interactive user traverse)", ex.Message));
            }
        }
    }

    // --- Phase A: NTFS removes for interactive user (background) ---

    private async Task SyncPhaseAAsync(
        string iuSid,
        AppDatabase database,
        List<GrantedPathEntry> pendingRemoves,
        List<GrantedPathEntry> pendingTraverseRemoves,
        HashSet<(string Path, bool IsDeny)> successfulRemoves,
        HashSet<string> successfulTraverseRemoves,
        List<(string Path, string Error)> errors)
    {
        await Task.Run(() =>
        {
            foreach (var entry in pendingRemoves)
            {
                if (entry.IsDeny)
                    continue; // deny grants are container-specific; never synced to interactive user
                if (!successfulRemoves.Contains((entry.Path, entry.IsDeny)))
                    continue;
                try
                {
                    _aclService.RevertGrant(entry.Path, iuSid, entry.IsDeny);
                }
                catch (Exception ex)
                {
                    _log.Warn($"Sync: failed to revert interactive user grant for '{entry.Path}': {ex.Message}");
                    errors.Add(($"{entry.Path} [interactive user]", ex.Message));
                }
            }

            foreach (var entry in pendingTraverseRemoves)
            {
                if (!successfulTraverseRemoves.Contains(entry.Path))
                    continue;
                var iuEntry = FindInteractiveTraverseEntry(database, iuSid, entry.Path);
                if (iuEntry == null)
                    continue;
                try
                {
                    _userTraverseService.RevertTraverseAccessForPath(iuSid, iuEntry, database);
                }
                catch (Exception ex)
                {
                    _log.Warn($"Sync: failed to revert interactive user traverse for '{entry.Path}': {ex.Message}");
                    errors.Add(($"{entry.Path} [interactive user traverse]", ex.Message));
                }
            }
        });
    }

    // --- Pre-check: detect paths where interactive user already has effective rights ---

    private async Task<HashSet<string>> ComputeAlreadyCoveredPathsAsync(
        string iuSid,
        List<GrantedPathEntry> pendingAdds,
        List<GrantedPathEntry> pendingModifications)
    {
        var alreadyCoveredPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await Task.Run(() =>
        {
            foreach (var entry in pendingAdds.Concat(pendingModifications))
            {
                if (entry.IsDeny)
                    continue;
                if (!_aclPermission.NeedsPermissionGrant(entry.Path, iuSid, ComputeRequiredRights(entry)))
                    alreadyCoveredPaths.Add(entry.Path);
            }
        });
        return alreadyCoveredPaths;
    }

    // --- Phase B: DB sync for interactive user (UI thread) ---

    private void SyncPhaseB(
        string iuSid,
        AppDatabase database,
        List<GrantedPathEntry> pendingAdds,
        List<GrantedPathEntry> pendingModifications,
        List<GrantedPathEntry> pendingTraverseAdds,
        List<GrantedPathEntry> pendingUntrackGrants,
        List<GrantedPathEntry> pendingUntrackTraverse,
        HashSet<(string Path, bool IsDeny)> successfulRemoves,
        HashSet<string> successfulTraverseRemoves,
        HashSet<string> alreadyCoveredPaths,
        List<(string Path, string Error)> errors)
    {
        var iuEntries = database.GetOrCreateAccount(iuSid).Grants;

        // Remove allow entries for successfully-reverted NTFS grants (deny grants are never synced).
        iuEntries.RemoveAll(e =>
            e is { IsTraverseOnly: false, IsDeny: false } && successfulRemoves.Contains((e.Path, false)));
        iuEntries.RemoveAll(e =>
            e.IsTraverseOnly && successfulTraverseRemoves.Contains(e.Path));

        // Untracks: DB-only removal (allow grants only).
        foreach (var entry in pendingUntrackGrants)
        {
            if (entry.IsDeny)
                continue;
            iuEntries.RemoveAll(e =>
                e is { IsTraverseOnly: false, IsDeny: false } &&
                string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var entry in pendingUntrackTraverse)
        {
            iuEntries.RemoveAll(e =>
                e.IsTraverseOnly &&
                string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
        }

        // Add new allow grants (or update SavedRights if the path already exists).
        // Deny grants are container-specific and are never synced to the interactive user.
        // Also skip paths where the interactive user already has the necessary effective rights.
        foreach (var entry in pendingAdds)
        {
            if (entry.IsDeny)
                continue;
            if (alreadyCoveredPaths.Contains(entry.Path))
                continue;
            var existing = iuEntries.FirstOrDefault(e =>
                string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase) &&
                e is { IsDeny: false, IsTraverseOnly: false });
            if (existing != null)
                existing.SavedRights = entry.SavedRights;
            else
                iuEntries.Add(new GrantedPathEntry { Path = entry.Path, SavedRights = entry.SavedRights });
        }

        // Ensure allow modifications have a matching DB entry (may not exist if grant pre-dates the sync feature).
        // Skip paths where the interactive user already has the necessary effective rights.
        foreach (var entry in pendingModifications)
        {
            if (entry.IsDeny)
                continue;
            if (alreadyCoveredPaths.Contains(entry.Path))
                continue;
            var existing = iuEntries.FirstOrDefault(e =>
                string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase) &&
                e is { IsDeny: false, IsTraverseOnly: false });
            if (existing != null)
                existing.SavedRights = entry.SavedRights;
            else
                iuEntries.Add(new GrantedPathEntry { Path = entry.Path, SavedRights = entry.SavedRights });
        }

        // Add new traverse entries.
        foreach (var entry in pendingTraverseAdds)
        {
            bool exists = iuEntries.Any(e =>
                e.IsTraverseOnly &&
                string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
            if (!exists)
                iuEntries.Add(new GrantedPathEntry { Path = entry.Path, IsTraverseOnly = true });
        }

        database.RemoveAccountIfEmpty(iuSid);

        try
        {
            _sessionSaver.SaveConfig();
        }
        catch (Exception ex)
        {
            _log.Error("Sync: failed to persist interactive user grants", ex);
            errors.Add(("(database - interactive user)", ex.Message));
        }
    }

    // --- Phase C: NTFS adds/modifications for interactive user (background) ---

    private async Task SyncPhaseCAsync(
        string iuSid,
        AppDatabase database,
        List<GrantedPathEntry> pendingAdds,
        List<GrantedPathEntry> pendingModifications,
        List<GrantedPathEntry> pendingTraverseAdds,
        List<GrantedPathEntry> pendingTraverseFixes,
        HashSet<string> alreadyCoveredPaths,
        List<(string Path, string Error)> errors)
    {
        await Task.Run(() =>
        {
            foreach (var entry in pendingAdds)
            {
                if (entry.IsDeny)
                    continue;
                if (alreadyCoveredPaths.Contains(entry.Path))
                    continue;
                try
                {
                    ApplyGrantForSid(entry, iuSid);
                }
                catch (Exception ex)
                {
                    _log.Warn($"Sync: failed to apply interactive user grant for '{entry.Path}': {ex.Message}");
                    errors.Add(($"{entry.Path} [interactive user]", ex.Message));
                }
            }

            foreach (var entry in pendingModifications)
            {
                if (entry.IsDeny)
                    continue;
                if (alreadyCoveredPaths.Contains(entry.Path))
                    continue;
                try
                {
                    _aclService.RevertGrant(entry.Path, iuSid, isDeny: false);
                    ApplyGrantForSid(entry, iuSid);
                }
                catch (Exception ex)
                {
                    _log.Warn($"Sync: failed to modify interactive user grant for '{entry.Path}': {ex.Message}");
                    errors.Add(($"{entry.Path} [interactive user]", ex.Message));
                }
            }

            foreach (var entry in pendingTraverseAdds)
            {
                try
                {
                    _userTraverseService.EnsureTraverseAccess(iuSid, entry.Path);
                }
                catch (Exception ex)
                {
                    _log.Warn($"Sync: failed to apply interactive user traverse for '{entry.Path}': {ex.Message}");
                    errors.Add(($"{entry.Path} [interactive user traverse]", ex.Message));
                }
            }

            foreach (var entry in pendingTraverseFixes)
            {
                try
                {
                    var (_, visitedPaths) = _userTraverseService.EnsureTraverseAccess(iuSid, entry.Path);
                    if (visitedPaths.Count > 0)
                    {
                        var iuEntry = database.GetAccount(iuSid)?.Grants.FirstOrDefault(e =>
                            e.IsTraverseOnly &&
                            string.Equals(e.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
                        if (iuEntry != null)
                            iuEntry.AllAppliedPaths = visitedPaths;
                    }
                }
                catch (Exception ex)
                {
                    _log.Warn($"Sync: failed to fix interactive user traverse for '{entry.Path}': {ex.Message}");
                    errors.Add(($"{entry.Path} [interactive user traverse fix]", ex.Message));
                }
            }
        });
    }

    // --- Helpers ---

    private static GrantedPathEntry? FindInteractiveTraverseEntry(AppDatabase database, string iuSid, string path)
    {
        return database.GetAccount(iuSid)?.Grants.FirstOrDefault(e =>
            e.IsTraverseOnly &&
            string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Computes the <see cref="FileSystemRights"/> mask that corresponds to the rights
    /// in <paramref name="entry"/> (for allow grants). Used when checking whether the
    /// interactive user already has the necessary effective rights before syncing.
    /// </summary>
    private static FileSystemRights ComputeRequiredRights(GrantedPathEntry entry)
    {
        var rights = GrantedPathAclService.ReadRightsMask;
        if (entry.SavedRights == null)
            return rights;
        if (entry.SavedRights.Execute)
            rights |= GrantedPathAclService.ExecuteRightsMask;
        if (entry.SavedRights.Write)
            rights |= GrantedPathAclService.WriteRightsMask;
        if (entry.SavedRights.Special)
            rights |= GrantedPathAclService.SpecialRightsMask;
        return rights;
    }

    /// <summary>
    /// Applies a grant ACE for <paramref name="sid"/> using the rights recorded in
    /// <paramref name="entry"/>. Ownership changes are always skipped — ownership is
    /// SID-specific and must not be transferred to the interactive user during sync.
    /// </summary>
    private void ApplyGrantForSid(GrantedPathEntry entry, string sid)
    {
        if (entry.SavedRights == null)
        {
            if (entry.IsDeny)
                _aclService.ApplyDenyRights(entry.Path, sid, new DenyRights(false, false));
            else
                _aclService.ApplyReadOnlyGrant(entry.Path, sid);
            return;
        }

        if (entry.IsDeny)
            _aclService.ApplyDenyRights(entry.Path, sid, new DenyRights(
                Read: entry.SavedRights.Read,
                Execute: entry.SavedRights.Execute));
        else
            _aclService.ApplyAllowRights(entry.Path, sid, new AllowRights(
                Execute: entry.SavedRights.Execute,
                Write: entry.SavedRights.Write,
                Special: entry.SavedRights.Special));
    }
}