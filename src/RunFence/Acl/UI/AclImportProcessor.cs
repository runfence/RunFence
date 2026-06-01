using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Persistence;
using RunFence.Acl.Traverse;
using RunFence.Acl.UI.ImportExport;

namespace RunFence.Acl.UI;

/// <summary>
/// Processes grant and traverse import data from <see cref="AclManagerExportImport.ExportData"/>
/// into <see cref="AclManagerPendingChanges"/>. Separated from export/dialog concerns.
/// </summary>
public class AclImportProcessor(
    ILoggingService log,
    IDatabaseProvider databaseProvider,
    GrantTraversePathResolver traversePathResolver,
    ISpecificContainerAceConflictDetector specificContainerAceConflictDetector) : IAclImportProcessor
{
    /// <summary>
    /// Imports grant and traverse entries from <paramref name="exportData"/> into
    /// <paramref name="pending"/>. Rolls back all changes on failure (added entries and
    /// cancelled pending removals). Returns added-state plus non-fatal skipped-entry warnings.
    /// </summary>
    public AclImportResult ProcessImport(
        AclImportRequest request)
    {
        var exportData = request.ExportData;
        var pending = request.Pending;
        var sid = request.Sid;
        var isContainer = request.IsContainer;
        if (exportData.Version != 1)
            return AclImportResult.Empty;

        var snapshot = pending.CaptureSnapshot();
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
            pending.RestoreFromSnapshot(snapshot);
            throw;
        }

        var anyAdded = !SnapshotEquals(pending.Grants.GetPendingAddsSnapshot(), snapshot.PendingAdds) ||
                       !SnapshotEquals(pending.Traverse.GetPendingAddsSnapshot(), snapshot.PendingTraverseAdds) ||
                       !SnapshotEquals(pending.Grants.GetPendingRemovesSnapshot(), snapshot.PendingRemoves) ||
                       !SnapshotEquals(pending.Traverse.GetPendingRemovesSnapshot(), snapshot.PendingTraverseRemoves) ||
                       !SnapshotEquals(pending.Grants.GetPendingUntrackSnapshot(), snapshot.PendingUntrackGrants) ||
                       !SnapshotEquals(pending.Traverse.GetPendingUntrackSnapshot(), snapshot.PendingUntrackTraverse);
        return new AclImportResult(anyAdded, warnings);
    }


    private void ImportGrants(
        ImportExport.AclExportData exportData,
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
                pending.Grants.CancelGrantRemoval(normalized, g.IsDeny);
                pending.Grants.RemoveUntrackedGrant(normalized, g.IsDeny);
                continue;
            }

            if (pending.Grants.IsPendingAdd(normalized, g.IsDeny))
                continue;

            // Build from mode defaults and override only the configurable bits, enforcing always-on
            // bits (Deny: Write+Special always on; Allow: Read always on) regardless of import data.
            var savedRights = g.IsDeny
                ? SavedRightsState.DefaultForMode(true, own: g.Owner) with { Execute = g.Execute, Read = g.Read }
                : SavedRightsState.DefaultForMode(false, own: g.Owner) with { Execute = g.Execute, Write = g.Write, Special = g.Special };
            savedRights = AclHelper.ClearBlockedGrantOwner(sid, isContainer, savedRights)!;
            var entry = new GrantedPathEntry { Path = normalized, IsDeny = g.IsDeny, SavedRights = savedRights };
            pending.Grants.AddGrant(entry);
            if (!g.IsDeny)
                acceptedAllowGrantPaths.Add(normalized);
        }
    }

    private void ImportTraverse(
        ImportExport.AclExportData exportData,
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
                pending.Traverse.CancelTraverseRemoval(normalized);
                pending.Traverse.RemoveUntrackedTraverse(normalized);
                if (IsTraverseAlreadyPresent(pending, sid, normalized))
                    continue;

                var entry = new GrantedPathEntry { Path = normalized, IsTraverseOnly = true };
                pending.Traverse.AddTraverse(entry);
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
            pending.Traverse.AddTraverse(entry);
        }
    }

    private bool IsTraverseAlreadyPresent(AclManagerPendingChanges pending, string sid, string normalizedPath) =>
        pending.Traverse.ExistsTraverseInDbOrPending(databaseProvider.GetDatabase(), sid, normalizedPath, checkUntrack: false);

    private bool HasOppositeModeConflictInEffectiveState(
        AclManagerPendingChanges pending,
        string sid,
        string normalizedPath,
        bool importIsDeny)
    {
        var oppositeMode = !importIsDeny;
        if (pending.Grants.IsPendingAdd(normalizedPath, oppositeMode))
            return true;

        if (pending.Grants.GetPendingModificationsSnapshot().Values.Any(m =>
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
            if (pending.Grants.IsPendingRemove(dbEntry.Path, dbEntry.IsDeny) || pending.Grants.IsUntrackGrant(dbEntry.Path, dbEntry.IsDeny))
                continue;

            var effectiveMode = pending.Grants.TryGetPendingModification(dbEntry.Path, dbEntry.IsDeny, out var mod)
                ? mod!.NewIsDeny
                : dbEntry.IsDeny;
            if (effectiveMode == oppositeMode)
                return true;
        }

        return false;
    }

    private static bool SnapshotEquals<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> current,
        IReadOnlyDictionary<TKey, TValue> snapshot)
        where TKey : notnull
    {
        if (current.Count != snapshot.Count)
            return false;

        foreach (var item in snapshot)
        {
            if (!current.TryGetValue(item.Key, out var value))
                return false;
            if (!SnapshotValuesEqual(value, item.Value))
                return false;
        }

        return true;
    }

    private static bool SnapshotValuesEqual<TValue>(TValue current, TValue snapshot)
        => (current, snapshot) switch
        {
            (GrantedPathEntry currentEntry, GrantedPathEntry snapshotEntry) =>
                EntriesEqual(currentEntry, snapshotEntry),
            (PendingModification currentModification, PendingModification snapshotModification) =>
                ModificationsEqual(currentModification, snapshotModification),
            (PendingConfigMove currentMove, PendingConfigMove snapshotMove) =>
                string.Equals(currentMove.TargetConfigPath, snapshotMove.TargetConfigPath, StringComparison.OrdinalIgnoreCase) &&
                EntriesEqual(currentMove.Entry, snapshotMove.Entry),
            _ => EqualityComparer<TValue>.Default.Equals(current, snapshot)
        };

    private static bool ModificationsEqual(PendingModification current, PendingModification snapshot)
        => EntriesEqual(current.Entry, snapshot.Entry) &&
           current.WasIsDeny == snapshot.WasIsDeny &&
           current.WasOwn == snapshot.WasOwn &&
           current.NewIsDeny == snapshot.NewIsDeny &&
           current.NewRights == snapshot.NewRights &&
           current.WasRights == snapshot.WasRights &&
           string.Equals(current.WasPreviousSaclLabel, snapshot.WasPreviousSaclLabel, StringComparison.Ordinal);

    private static bool EntriesEqual(GrantedPathEntry current, GrantedPathEntry snapshot)
        => string.Equals(current.Path, snapshot.Path, StringComparison.OrdinalIgnoreCase) &&
           current.IsTraverseOnly == snapshot.IsTraverseOnly &&
           current.IsDeny == snapshot.IsDeny &&
           SequenceEqual(current.AllAppliedPaths, snapshot.AllAppliedPaths) &&
           current.SavedRights == snapshot.SavedRights &&
           string.Equals(current.OwnerContainerSid, snapshot.OwnerContainerSid, StringComparison.OrdinalIgnoreCase) &&
           SequenceEqual(current.SourceSids, snapshot.SourceSids) &&
           string.Equals(current.PreviousSaclLabel, snapshot.PreviousSaclLabel, StringComparison.Ordinal);

    private static bool SequenceEqual(IReadOnlyList<string>? current, IReadOnlyList<string>? snapshot)
        => (current ?? []).SequenceEqual(snapshot ?? [], StringComparer.OrdinalIgnoreCase);
}
