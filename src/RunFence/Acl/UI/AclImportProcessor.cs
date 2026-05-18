using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Acl.Traverse;

namespace RunFence.Acl.UI;

/// <summary>
/// Processes grant and traverse import data from <see cref="AclManagerExportImport.ExportData"/>
/// into <see cref="AclManagerPendingChanges"/>. Separated from export/dialog concerns.
/// </summary>
public class AclImportProcessor(
    ILoggingService log,
    IDatabaseProvider databaseProvider,
    GrantTraversePathResolver traversePathResolver,
    ISpecificContainerAceConflictDetector specificContainerAceConflictDetector)
{
    /// <summary>
    /// Imports grant and traverse entries from <paramref name="exportData"/> into
    /// <paramref name="pending"/>. Rolls back all changes on failure (added entries and
    /// cancelled pending removals). Returns added-state plus non-fatal skipped-entry warnings.
    /// </summary>
    public AclImportResult ProcessImport(
        AclManagerExportImport.ExportData exportData,
        AclManagerPendingChanges pending,
        string sid,
        bool isContainer)
    {
        if (exportData.Version != 1)
            return AclImportResult.Empty;

        var pendingAddsSnapshot = pending.PendingAdds.ToList();
        var pendingTraverseAddsSnapshot = pending.PendingTraverseAdds.ToList();
        var pendingRemovesSnapshot = pending.PendingRemoves.ToList();
        var pendingTraverseRemovesSnapshot = pending.PendingTraverseRemoves.ToList();
        var pendingUntrackGrantsSnapshot = pending.PendingUntrackGrants.ToList();
        var pendingUntrackTraverseSnapshot = pending.PendingUntrackTraverse.ToList();
        var warnings = new List<AclImportWarning>();

        try
        {
            var acceptedAllowGrantPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ImportGrants(exportData, pending, sid, isContainer, acceptedAllowGrantPaths, warnings);
            ImportTraverse(exportData, pending, sid, isContainer, acceptedAllowGrantPaths);
        }
        catch (Exception ex)
        {
            log.Error("ProcessImport: failed to process import entry — rolling back", ex);
            RestoreSnapshot(pending.PendingAdds, pendingAddsSnapshot);
            RestoreSnapshot(pending.PendingTraverseAdds, pendingTraverseAddsSnapshot);
            RestoreSnapshot(pending.PendingRemoves, pendingRemovesSnapshot);
            RestoreSnapshot(pending.PendingTraverseRemoves, pendingTraverseRemovesSnapshot);
            RestoreSnapshot(pending.PendingUntrackGrants, pendingUntrackGrantsSnapshot);
            RestoreSnapshot(pending.PendingUntrackTraverse, pendingUntrackTraverseSnapshot);
            throw;
        }

        var anyAdded = !SnapshotEquals(pending.PendingAdds, pendingAddsSnapshot) ||
                       !SnapshotEquals(pending.PendingTraverseAdds, pendingTraverseAddsSnapshot) ||
                       !SnapshotEquals(pending.PendingRemoves, pendingRemovesSnapshot) ||
                       !SnapshotEquals(pending.PendingTraverseRemoves, pendingTraverseRemovesSnapshot) ||
                       !SnapshotEquals(pending.PendingUntrackGrants, pendingUntrackGrantsSnapshot) ||
                       !SnapshotEquals(pending.PendingUntrackTraverse, pendingUntrackTraverseSnapshot);
        return new AclImportResult(anyAdded, warnings);
    }

    private void ImportGrants(
        AclManagerExportImport.ExportData exportData,
        AclManagerPendingChanges pending,
        string sid,
        bool isContainer,
        HashSet<string> acceptedAllowGrantPaths,
        List<AclImportWarning> warnings)
    {
        var importedGrantModes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var g in exportData.Grants ?? [])
        {
            if (string.IsNullOrEmpty(g.Path))
                continue;
            var normalized = Path.GetFullPath(g.Path);

            if (importedGrantModes.TryGetValue(normalized, out var importedIsDeny) && importedIsDeny != g.IsDeny)
            {
                log.Warn($"Import grant conflict rejected: '{normalized}' has both allow and deny in the same import file.");
                continue;
            }
            importedGrantModes[normalized] = g.IsDeny;

            if (HasOppositeModeConflictInEffectiveState(pending, sid, normalized, g.IsDeny))
            {
                log.Warn($"Import grant conflict rejected: '{normalized}' conflicts with an existing opposite-mode entry.");
                continue;
            }

            var conflictMessage = AclConflictWarningHelper.GetConflictMessage(sid, normalized, g.IsDeny, specificContainerAceConflictDetector);
            if (conflictMessage != null)
            {
                log.Warn($"Import grant conflict rejected: '{normalized}' {conflictMessage}");
                warnings.Add(new AclImportWarning(normalized, conflictMessage));
                continue;
            }

            bool inDb = databaseProvider.GetDatabase().GetAccount(sid)?.Grants
                .Any(e => string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase)
                          && e.IsDeny == g.IsDeny && !e.IsTraverseOnly) == true;
            if (inDb)
            {
                // If pending removal, cancel the removal (import restores the entry).
                var key = (normalized, g.IsDeny);
                pending.PendingRemoves.Remove(key);
                pending.PendingUntrackGrants.Remove(key);
                continue;
            }

            if (pending.IsPendingAdd(normalized, g.IsDeny))
                continue;

            // Build from mode defaults and override only the configurable bits, enforcing always-on
            // bits (Deny: Write+Special always on; Allow: Read always on) regardless of import data.
            var savedRights = g.IsDeny
                ? SavedRightsState.DefaultForMode(true, own: g.Owner) with { Execute = g.Execute, Read = g.Read }
                : SavedRightsState.DefaultForMode(false, own: g.Owner) with { Execute = g.Execute, Write = g.Write, Special = g.Special };
            savedRights = AclHelper.ClearBlockedGrantOwner(sid, isContainer, savedRights)!;
            var entry = new GrantedPathEntry { Path = normalized, IsDeny = g.IsDeny, SavedRights = savedRights };
            pending.PendingAdds[(normalized, g.IsDeny)] = entry;
            if (!g.IsDeny)
                acceptedAllowGrantPaths.Add(normalized);
        }
    }

    private void ImportTraverse(
        AclManagerExportImport.ExportData exportData,
        AclManagerPendingChanges pending,
        string sid,
        bool isContainer,
        IReadOnlySet<string> acceptedAllowGrantPaths)
    {
        var traverseInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!isContainer)
        {
            foreach (var t in exportData.Traverse ?? [])
                if (!string.IsNullOrEmpty(t.Path))
                    traverseInFile.Add(Path.GetFullPath(t.Path));

            foreach (var t in exportData.Traverse ?? [])
            {
                if (string.IsNullOrEmpty(t.Path))
                    continue;
                var normalized = Path.GetFullPath(t.Path);
                pending.PendingTraverseRemoves.Remove(normalized);
                pending.PendingUntrackTraverse.Remove(normalized);
                if (IsTraverseAlreadyPresent(pending, sid, normalized))
                    continue;

                var entry = new GrantedPathEntry { Path = normalized, IsTraverseOnly = true };
                pending.PendingTraverseAdds[normalized] = entry;
            }
        }

        // Auto-add traverse for imported allow grants not already covered in file.
        foreach (var normalizedGrant in acceptedAllowGrantPaths)
        {
            var traversePath = traversePathResolver.GetTraversePath(normalizedGrant);
            if (traversePath == null)
                continue;

            if (traverseInFile.Contains(traversePath))
                continue;
            if (IsTraverseAlreadyPresent(pending, sid, traversePath))
                continue;

            var entry = new GrantedPathEntry { Path = traversePath, IsTraverseOnly = true };
            pending.PendingTraverseAdds[traversePath] = entry;
        }
    }

    private bool IsTraverseAlreadyPresent(AclManagerPendingChanges pending, string sid, string normalizedPath) =>
        pending.ExistsTraverseInDbOrPending(databaseProvider.GetDatabase(), sid, normalizedPath, checkUntrack: false);

    private bool HasOppositeModeConflictInEffectiveState(
        AclManagerPendingChanges pending,
        string sid,
        string normalizedPath,
        bool importIsDeny)
    {
        var oppositeMode = !importIsDeny;
        if (pending.IsPendingAdd(normalizedPath, oppositeMode))
            return true;

        if (pending.PendingModifications.Values.Any(m =>
                string.Equals(m.Entry.Path, normalizedPath, StringComparison.OrdinalIgnoreCase) &&
                m.NewIsDeny == oppositeMode))
            return true;

        var account = databaseProvider.GetDatabase().GetAccount(sid);
        if (account == null)
            return false;

        foreach (var dbEntry in account.Grants.Where(e =>
                     !e.IsTraverseOnly &&
                     string.Equals(e.Path, normalizedPath, StringComparison.OrdinalIgnoreCase)))
        {
            if (pending.IsPendingRemove(dbEntry.Path, dbEntry.IsDeny) || pending.IsUntrackGrant(dbEntry.Path, dbEntry.IsDeny))
                continue;

            var effectiveMode = pending.PendingModifications.TryGetValue((dbEntry.Path, dbEntry.IsDeny), out var mod)
                ? mod.NewIsDeny
                : dbEntry.IsDeny;
            if (effectiveMode == oppositeMode)
                return true;
        }

        return false;
    }

    private static void RestoreSnapshot<TKey, TValue>(
        Dictionary<TKey, TValue> target,
        IEnumerable<KeyValuePair<TKey, TValue>> snapshot)
        where TKey : notnull
    {
        target.Clear();
        foreach (var item in snapshot)
            target[item.Key] = item.Value;
    }

    private static bool SnapshotEquals<TKey, TValue>(
        Dictionary<TKey, TValue> current,
        IEnumerable<KeyValuePair<TKey, TValue>> snapshot)
        where TKey : notnull
    {
        var snapshotList = snapshot.ToList();
        if (current.Count != snapshotList.Count)
            return false;

        foreach (var item in snapshotList)
        {
            if (!current.TryGetValue(item.Key, out var value))
                return false;
            if (!EqualityComparer<TValue>.Default.Equals(value, item.Value))
                return false;
        }

        return true;
    }

}
