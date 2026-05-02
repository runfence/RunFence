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
    GrantTraversePathResolver traversePathResolver)
{
    /// <summary>
    /// Imports grant and traverse entries from <paramref name="exportData"/> into
    /// <paramref name="pending"/>. Rolls back all changes on failure (added entries and
    /// cancelled pending removals). Returns true if any entries were added.
    /// </summary>
    public bool ProcessImport(
        AclManagerExportImport.ExportData exportData,
        AclManagerPendingChanges pending,
        string sid,
        bool isContainer)
    {
        if (exportData.Version != 1)
            return false;

        var addedGrantKeys = new List<(string Path, bool IsDeny)>();
        var addedTraverseKeys = new List<string>();
        var cancelledRemoves = new List<((string Path, bool IsDeny) Key, GrantedPathEntry Entry)>();

        try
        {
            ImportGrants(exportData, pending, sid, isContainer, addedGrantKeys, cancelledRemoves);
            ImportTraverse(exportData, pending, sid, addedTraverseKeys);
        }
        catch (Exception ex)
        {
            log.Error("ProcessImport: failed to process import entry — rolling back", ex);
            foreach (var key in addedGrantKeys)
                pending.PendingAdds.Remove(key);
            foreach (var key in addedTraverseKeys)
                pending.PendingTraverseAdds.Remove(key);
            foreach (var (key, entry) in cancelledRemoves)
                pending.PendingRemoves[key] = entry;
            throw;
        }

        return addedGrantKeys.Count > 0 || addedTraverseKeys.Count > 0;
    }

    private void ImportGrants(
        AclManagerExportImport.ExportData exportData,
        AclManagerPendingChanges pending,
        string sid,
        bool isContainer,
        List<(string Path, bool IsDeny)> addedGrantKeys,
        List<((string Path, bool IsDeny) Key, GrantedPathEntry Entry)> cancelledRemoves)
    {
        foreach (var g in exportData.Grants ?? [])
        {
            if (string.IsNullOrEmpty(g.Path))
                continue;
            var normalized = Path.GetFullPath(g.Path);

            bool inDb = databaseProvider.GetDatabase().GetAccount(sid)?.Grants
                .Any(e => string.Equals(e.Path, normalized, StringComparison.OrdinalIgnoreCase)
                          && e.IsDeny == g.IsDeny && !e.IsTraverseOnly) == true;
            if (inDb)
            {
                // If pending removal, cancel the removal (import restores the entry).
                var key = (normalized, g.IsDeny);
                if (pending.PendingRemoves.TryGetValue(key, out var removed))
                {
                    cancelledRemoves.Add((key, removed));
                    pending.PendingRemoves.Remove(key);
                }
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
            addedGrantKeys.Add((normalized, g.IsDeny));
        }
    }

    private void ImportTraverse(
        AclManagerExportImport.ExportData exportData,
        AclManagerPendingChanges pending,
        string sid,
        List<string> addedTraverseKeys)
    {
        var traverseInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in exportData.Traverse ?? [])
            if (!string.IsNullOrEmpty(t.Path))
                traverseInFile.Add(Path.GetFullPath(t.Path));

        foreach (var t in exportData.Traverse ?? [])
        {
            if (string.IsNullOrEmpty(t.Path))
                continue;
            var normalized = Path.GetFullPath(t.Path);
            if (IsTraverseAlreadyPresent(pending, sid, normalized))
                continue;

            var entry = new GrantedPathEntry { Path = normalized, IsTraverseOnly = true };
            pending.PendingTraverseAdds[normalized] = entry;
            addedTraverseKeys.Add(normalized);
        }

        // Auto-add traverse for imported allow grants not already covered in file.
        foreach (var g in exportData.Grants ?? [])
        {
            if (g.IsDeny || string.IsNullOrEmpty(g.Path))
                continue;
            var normalizedGrant = Path.GetFullPath(g.Path);
            var traversePath = traversePathResolver.GetTraversePath(normalizedGrant);
            if (traversePath == null)
                continue;

            if (traverseInFile.Contains(traversePath))
                continue;
            if (IsTraverseAlreadyPresent(pending, sid, traversePath))
                continue;

            var entry = new GrantedPathEntry { Path = traversePath, IsTraverseOnly = true };
            pending.PendingTraverseAdds[traversePath] = entry;
            addedTraverseKeys.Add(traversePath);
    }

}

    private bool IsTraverseAlreadyPresent(AclManagerPendingChanges pending, string sid, string normalizedPath) =>
        pending.ExistsTraverseInDbOrPending(databaseProvider.GetDatabase(), sid, normalizedPath, checkUntrack: false);
}
