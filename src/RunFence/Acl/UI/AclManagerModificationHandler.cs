using RunFence.Acl.UI.Forms;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

/// <summary>
/// Handles add/remove operations, scan, untrack, fix-ACLs, drag-drop, and shell file-drop
/// events for <see cref="AclManagerDialog"/>.
/// </summary>
public class AclManagerModificationHandler(
    IAppConfigService appConfigService,
    ILoggingService log,
    IAclManagerScanService scanService,
    IDatabaseProvider databaseProvider,
    AclManagerTraverseHelper traverseHelper,
    AclManagerDragDropHandler dragDropHandler,
    AclManagerActionOrchestrator actionHandler,
    IReparsePointPromptHelper reparsePointHelper)
{
    private readonly AclManagerTraverseHelper _traverseHelper = traverseHelper;
    private readonly AclManagerDragDropHandler _dragDropHandler = dragDropHandler;
    private readonly AclManagerActionOrchestrator _actionHandler = actionHandler;
    private IAclManagerDialogHost _dialogHost = null!;
    private string _sid = null!;
    private AclManagerPendingChanges _pending = null!;
    private AclManagerDialogControls _controls = null!;
    private Action _refreshGrantsGrid = null!;
    private Action _refreshTraverseGrid = null!;
    private Action _updateActionButtons = null!;

    public CancellationTokenSource? CancelScanCts { get; private set; }

    public void Initialize(
        IAclManagerDialogHost dialogHost,
        string sid,
        AclManagerPendingChanges pending,
        AclManagerDialogControls controls,
        Action refreshGrantsGrid,
        Action refreshTraverseGrid,
        Action updateActionButtons)
    {
        _dialogHost = dialogHost;
        _sid = sid;
        _pending = pending;
        _controls = controls;
        _refreshGrantsGrid = refreshGrantsGrid;
        _refreshTraverseGrid = refreshTraverseGrid;
        _updateActionButtons = updateActionButtons;
    }

    // --- Scan ---

    public async void ScanFolder()
    {
        string selectedPath;
        using (var fbd = new FolderBrowserDialog())
        {
            fbd.Description = "Select folder to scan for existing grants and traverse paths";
            fbd.UseDescriptionForTitle = true;
            if (fbd.ShowDialog(_dialogHost) != DialogResult.OK)
                return;
            selectedPath = fbd.SelectedPath;
        }

        _dialogHost.Enabled = false;
        _dialogHost.Cursor = Cursors.WaitCursor;
        _controls.ScanStatusLabel.Text = "Scanning...";
        _controls.ScanStatusLabel.Visible = true;

        CancelScanCts = new CancellationTokenSource();
        int updated;
        try
        {
            var progress = new Progress<long>(n =>
            {
                if (!_dialogHost.IsDisposed)
                    _controls.ScanStatusLabel.Text = $"Scanning... ({n} items scanned)";
            });
            updated = await scanService.ScanAsync(selectedPath, _sid, progress, CancelScanCts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            log.Error($"Scan failed for '{selectedPath}'", ex);
            MessageBox.Show($"Scan failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally
        {
            CancelScanCts?.Dispose();
            CancelScanCts = null;
            if (!_dialogHost.IsDisposed)
            {
                _controls.ScanStatusLabel.Visible = false;
                _dialogHost.Cursor = Cursors.Default;
                _dialogHost.Enabled = true;
            }
        }

        if (updated == 0)
            MessageBox.Show("No new grants or traverse paths found.", "Scan Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
        else
        {
            // Clear pending adds/modifications for paths the scan just added to the DB.
            // Without this, the grid would show the same path twice: once as a pending add
            // and once as a committed DB entry.
            var database = databaseProvider.GetDatabase();
            var accountGrants = database.GetAccount(_sid)?.Grants;
            if (accountGrants != null)
            {
                var dbPaths = new HashSet<(string, bool)>(
                    accountGrants.Select(g => (Path.GetFullPath(g.Path), g.IsDeny)),
                    new GrantPathKeyComparer());
                var pendingAddKeysToRemove = _pending.PendingAdds.Keys
                    .Where(k => dbPaths.Contains(k)).ToList();
                foreach (var key in pendingAddKeysToRemove)
                    _pending.PendingAdds.Remove(key);
                var pendingModKeysToRemove = _pending.PendingModifications.Keys
                    .Where(k => dbPaths.Contains(k)).ToList();
                foreach (var key in pendingModKeysToRemove)
                    _pending.PendingModifications.Remove(key);
            }

            _refreshGrantsGrid();
            _refreshTraverseGrid();
            _updateActionButtons();
        }
    }

    // --- Add / Remove ---

    public void AddFile()
        => AddPath(isFolder: false, isTraverse: _controls.TabControl.SelectedTab == _controls.TraverseTab);

    public void AddFolder()
        => AddPath(isFolder: true, isTraverse: _controls.TabControl.SelectedTab == _controls.TraverseTab);

    public void AddTraverseFile()
        => AddPath(isFolder: false, isTraverse: true);

    public void AddTraverseFolder()
        => AddPath(isFolder: true, isTraverse: true);

    private void AddPath(bool isFolder, bool isTraverse)
    {
        if (isTraverse)
        {
            var error = _traverseHelper.AddTraversePath(isFolder, _dialogHost);
            if (error != null)
                MessageBox.Show(error, "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _refreshTraverseGrid();
            _updateActionButtons();
        }
        else
        {
            AddGrantPath(isFolder);
        }
    }

    // Toolbar "Add File" / "Add Folder" always adds as Allow by design: the user explicitly
    // chooses to grant access. Deny grants are added via shell drag-drop or context menu,
    // where the mode prompt makes the distinction explicit.
    private void AddGrantPath(bool isFolder)
    {
        string? selectedPath;
        if (isFolder)
        {
            using var fbd = new FolderBrowserDialog();
            fbd.Description = "Select folder to add Allow grant";
            fbd.UseDescriptionForTitle = true;
            if (fbd.ShowDialog(_dialogHost) != DialogResult.OK)
                return;
            selectedPath = fbd.SelectedPath;
        }
        else
        {
            using var ofd = new OpenFileDialog();
            ofd.Title = "Select file to add Allow grant";
            ofd.Filter = "All files (*.*)|*.*";
            FileDialogHelper.AddInteractiveUserCustomPlaces(ofd);
            if (ofd.ShowDialog(_dialogHost) != DialogResult.OK)
                return;
            selectedPath = ofd.FileName;
        }

        var pathsToAdd = reparsePointHelper.ResolveForAdd(selectedPath, _dialogHost);
        foreach (var p in pathsToAdd)
        {
            var error = _actionHandler.AddGrantPathDirect(p, isDeny: false);
            if (error != null)
                MessageBox.Show(error, "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        _updateActionButtons();
    }

    public void RemoveSelected()
    {
        if (_controls.TabControl.SelectedTab == _controls.TraverseTab)
        {
            var entries = AclManagerSectionHeader.ExpandSectionSelection(_controls.TraverseGrid)
                .Where(r => r.Tag is GrantedPathEntry)
                .Select(r => (GrantedPathEntry)r.Tag!)
                .ToList();
            if (entries.Count > 0)
            {
                _traverseHelper.RemoveTraversePaths(entries);
                _refreshTraverseGrid();
                _updateActionButtons();
            }

            return;
        }

        var selectedEntries = AclManagerSectionHeader.ExpandSectionSelection(_controls.GrantsGrid)
            .Select(r => (GrantedPathEntry)r.Tag!)
            .ToList();
        if (selectedEntries.Count == 0)
            return;

        int traverseBefore = _pending.PendingTraverseAdds.Count + _pending.PendingTraverseRemoves.Count;
        foreach (var entry in selectedEntries)
            _actionHandler.RemoveGrantPathDeferred(entry);
        int traverseAfter = _pending.PendingTraverseAdds.Count + _pending.PendingTraverseRemoves.Count;

        _refreshGrantsGrid();
        if (traverseAfter != traverseBefore)
            _refreshTraverseGrid();
        _updateActionButtons();
    }

    public void UntrackGrants()
    {
        var selectedEntries = AclManagerSectionHeader.ExpandSectionSelection(_controls.GrantsGrid)
            .Select(r => (GrantedPathEntry)r.Tag!)
            .ToList();
        if (selectedEntries.Count == 0)
            return;

        int traverseBefore = _pending.PendingTraverseAdds.Count + _pending.PendingTraverseRemoves.Count;
        foreach (var entry in selectedEntries)
            _actionHandler.UntrackGrantPath(entry);
        int traverseAfter = _pending.PendingTraverseAdds.Count + _pending.PendingTraverseRemoves.Count;

        _refreshGrantsGrid();
        if (traverseAfter != traverseBefore)
            _refreshTraverseGrid();
        _updateActionButtons();
    }

    public void UntrackTraverse()
    {
        var entries = AclManagerSectionHeader.ExpandSectionSelection(_controls.TraverseGrid)
            .Where(r => r.Tag is GrantedPathEntry)
            .Select(r => (GrantedPathEntry)r.Tag!)
            .ToList();
        if (entries.Count == 0)
            return;

        foreach (var entry in entries)
            _actionHandler.UntrackTraversePath(entry);

        _refreshTraverseGrid();
        _updateActionButtons();
    }

    public void FixAcls()
    {
        bool isTraverseTab = _controls.TabControl.SelectedTab == _controls.TraverseTab;
        var expandedRows = isTraverseTab
            ? AclManagerSectionHeader.ExpandSectionSelection(_controls.TraverseGrid)
            : AclManagerSectionHeader.ExpandSectionSelection(_controls.GrantsGrid);
        _actionHandler.FixAcls(expandedRows, isTraverseTab);
        if (isTraverseTab)
            _refreshTraverseGrid();
        else
            _refreshGrantsGrid();
        _updateActionButtons();
    }

    // --- Mouse drag ---

    public void HandleGrantsMouseDown(MouseEventArgs e)
        => _dragDropHandler.HandleMouseDown(e, _controls.GrantsGrid);

    public void HandleGrantsMouseMove(MouseEventArgs e)
        => _dragDropHandler.HandleMouseMove(e, _controls.GrantsGrid);

    public void HandleTraverseMouseDown(MouseEventArgs e)
        => _dragDropHandler.HandleMouseDown(e, _controls.TraverseGrid);

    public void HandleTraverseMouseMove(MouseEventArgs e)
        => _dragDropHandler.HandleMouseMove(e, _controls.TraverseGrid);

    public void HandleGrantsMouseUp(MouseEventArgs e)
    {
        if (_dragDropHandler.HandleMouseUp(e, _controls.GrantsGrid))
        {
            _refreshGrantsGrid();
            _updateActionButtons();
        }
    }

    public void HandleTraverseMouseUp(MouseEventArgs e)
    {
        if (_dragDropHandler.HandleMouseUp(e, _controls.TraverseGrid))
        {
            _refreshTraverseGrid();
            _updateActionButtons();
        }
    }

    // --- Shell file drop ---

    public void HandleGrantsFileDrop(string[] paths)
    {
        string? targetConfigPath = null;
        if (appConfigService.HasLoadedConfigs)
        {
            var cursorClient = _controls.GrantsGrid.PointToClient(Cursor.Position);
            var hitTest = _controls.GrantsGrid.HitTest(cursorClient.X, cursorClient.Y);
            if (hitTest.RowIndex >= 0)
                targetConfigPath = AclManagerSectionHeader.GetSectionConfigPath(_controls.GrantsGrid, hitTest.RowIndex);
        }

        var error = _actionHandler.HandleShellDropOnGrants(paths, targetConfigPath);
        if (error != null)
            MessageBox.Show(error, "Info", MessageBoxButtons.OK, MessageBoxIcon.Information);
        _updateActionButtons();
    }

    public void HandleTraverseFileDrop(string[] paths)
    {
        string pathText = paths.Length == 1 ? paths[0] : $"{paths.Length} paths";
        var confirm = MessageBox.Show(
            $"Add traverse access for:\n{pathText}",
            "Add Traverse", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (confirm == DialogResult.Yes)
            _actionHandler.HandleShellDropOnTraverse(paths);
        _updateActionButtons();
    }
}