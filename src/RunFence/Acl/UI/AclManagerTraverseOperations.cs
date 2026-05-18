using RunFence.Acl.Permissions;
using RunFence.Acl.Traverse;
using RunFence.Acl.UI.Forms;
using RunFence.Core.Infrastructure;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

/// <summary>
/// Handles traverse path mutation operations (add, remove, fix, ensure) for
/// <see cref="AclManagerDialog"/>. Writes are deferred to <see cref="AclManagerPendingChanges"/>;
/// no NTFS or DB writes occur until Apply.
/// </summary>
public class AclManagerTraverseOperations(
    IDatabaseProvider databaseProvider,
    IReparsePointPromptHelper reparsePointHelper,
    IAclPermissionService aclPermission,
    IFileSystemPathInfo pathInfo)
{
    private string _sid = null!;
    private AclManagerPendingChanges _pending = null!;
    private Lazy<IReadOnlyList<string>> _groupSids = null!;
    private Action _populateTraverseGrid = null!;

    public void Initialize(
        string sid,
        AclManagerPendingChanges pending,
        Lazy<IReadOnlyList<string>> groupSids,
        Action populateTraverseGrid)
    {
        _sid = sid;
        _pending = pending;
        _groupSids = groupSids;
        _populateTraverseGrid = populateTraverseGrid;
    }

    /// <summary>
    /// Adds a traverse path entry. Shows file/folder dialogs, validates, records to pending
    /// (no NTFS or DB write), and refreshes the grid.
    /// Returns an error message string when the path is already in the list; null on success or cancel.
    /// </summary>
    public string? AddTraversePath(bool isFolder, IWin32Window owner)
    {
        string? selectedPath;
        if (isFolder)
        {
            using var fbd = new FolderBrowserDialog();
            fbd.Description = "Select folder to grant traverse access";
            fbd.UseDescriptionForTitle = true;
            if (fbd.ShowDialog(owner) != DialogResult.OK)
                return null;
            selectedPath = fbd.SelectedPath;
        }
        else
        {
            using var ofd = new OpenFileDialog();
            ofd.Title = "Select file to grant traverse access";
            ofd.Filter = "All files (*.*)|*.*";
            FileDialogHelper.AddInteractiveUserCustomPlaces(ofd);
            if (ofd.ShowDialog(owner) != DialogResult.OK)
                return null;
            selectedPath = ofd.FileName;
        }

        if (string.IsNullOrEmpty(selectedPath))
            return null;

        var pathsToAdd = reparsePointHelper.ResolveForAdd(selectedPath, owner);
        return pathsToAdd.Aggregate<string, string?>(null, (current, p) => AddTraversePathDirect(p) ?? current);
    }

    /// <summary>
    /// Adds a traverse path entry for a known path (e.g. from shell drag-drop). Validates,
    /// records to pending (no NTFS or DB write), and refreshes the grid.
    /// Returns an error message string when the path is already in the list; null on success.
    /// </summary>
    public string? AddTraversePathDirect(string selectedPath)
    {
        if (string.IsNullOrEmpty(selectedPath))
            return null;

        var normalized = Path.GetFullPath(selectedPath);

        // Check for duplicate in DB or pending adds (excluding pending removes/untracks — those are being removed).
        if (_pending.ExistsTraverseInDbOrPending(databaseProvider.GetDatabase(), _sid, normalized))
            return "This path is already in the list.";

        // If this path was previously queued for removal or untrack, cancel it instead.
        if (_pending.PendingTraverseRemoves.Remove(normalized) ||
            _pending.PendingUntrackTraverse.Remove(normalized))
        {
            _populateTraverseGrid();
            return null;
        }

        var entry = new GrantedPathEntry { Path = normalized, IsTraverseOnly = true };
        _pending.PendingTraverseAdds[normalized] = entry;
        _populateTraverseGrid();
        return null;
    }

    /// <summary>
    /// Removes one or more traverse path entries (deferred). If the entry was a pending add,
    /// it is simply discarded. Otherwise the entry is added to <see cref="AclManagerPendingChanges.PendingTraverseRemoves"/>
    /// and removed from the grid immediately. No NTFS or DB writes occur until Apply.
    /// </summary>
    public void RemoveTraversePaths(IReadOnlyList<GrantedPathEntry> entries)
    {
        if (entries.Count == 0)
            return;
        foreach (var entry in entries)
        {
            var normalized = Path.GetFullPath(entry.Path);
            if (!_pending.PendingTraverseAdds.Remove(normalized))
                // Existing DB entry — queue for deferred removal.
                _pending.PendingTraverseRemoves[normalized] = entry;
            _pending.PendingTraverseConfigMoves.Remove(normalized);
        }

        _populateTraverseGrid();
    }

    /// <summary>
    /// Records traverse path entries as pending fix (deferred). The rows are shown in green
    /// to indicate the fix intent is recorded and will be applied. No NTFS writes occur until Apply.
    /// </summary>
    public void FixTraversePaths(IReadOnlyList<GrantedPathEntry> entries)
    {
        if (entries.Count == 0)
            return;
        foreach (var entry in entries)
            _pending.PendingTraverseFixes[Path.GetFullPath(entry.Path)] = entry;
        _populateTraverseGrid();
    }

    /// <summary>
    /// Ensures a traverse entry is pending for <paramref name="grantDir"/> for the current SID.
    /// Called automatically when an allow grant is added in the grants tab.
    /// Adds to <see cref="AclManagerPendingChanges.PendingTraverseAdds"/> if no traverse entry
    /// already exists in DB or pending. Cancels a pending removal if one exists for the same path.
    /// No NTFS writes occur until Apply.
    /// Returns true if the traverse grid should be repopulated (entry added or removal cancelled).
    /// </summary>
    public bool TryEnsureTraverseAccess(string grantDir)
    {
        var normalized = Path.GetFullPath(grantDir);

        // Already covered by a DB entry or pending add (not pending removal or untrack)?
        if (_pending.ExistsTraverseInDbOrPending(databaseProvider.GetDatabase(), _sid, normalized))
            return false;

        // If this was queued for removal, cancel the removal (re-activate it).
        if (_pending.PendingTraverseRemoves.Remove(normalized))
            return true;

        // Skip add when the SID already has effective traverse rights on this path.
        if (TraverseRightsHelper.HasEffectiveTraverseForGrantSid(normalized, _sid, _groupSids.Value, aclPermission, pathInfo))
            return false;

        var entry = new GrantedPathEntry { Path = normalized, IsTraverseOnly = true };
        _pending.PendingTraverseAdds[normalized] = entry;
        return true;
    }
}
