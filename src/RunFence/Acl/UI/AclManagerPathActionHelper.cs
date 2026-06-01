using RunFence.Acl.UI.Forms;
using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Acl.UI;

/// <summary>
/// Handles shell path actions (open folder, copy path, show properties) and grid event
/// routing (right-click selection, mouse-click deselection) for <see cref="AclManagerDialog"/>.
/// </summary>
public class AclManagerPathActionHelper(
    IShellHelper shellHelper)
{
    private IWin32Window _owner = null!;
    private AclManagerDialogControls _controls = null!;

    public void Initialize(IWin32Window owner, AclManagerDialogControls controls)
    {
        _owner = owner;
        _controls = controls;
    }

    // --- Path helpers ---

    private string? GetSelectedGrantPath()
    {
        var selected = _controls.GrantsGrid.SelectedRows.Cast<DataGridViewRow>()
            .Where(r => r.Tag is GrantedPathEntry).ToList();
        return selected.Count == 1 ? ((GrantedPathEntry)selected[0].Tag!).Path : null;
    }

    private string? GetSelectedTraversePath()
    {
        var selected = _controls.TraverseGrid.SelectedRows.Cast<DataGridViewRow>().ToList();
        return selected.Count == 1 && selected[0].Tag is GrantedPathEntry e ? e.Path : null;
    }

    // --- Shell path actions ---

    public void OpenFolderGrants() => OpenFolder(GetSelectedGrantPath());

    public void CopyPathGrants() => CopyPath(GetSelectedGrantPath());

    public void ShowPropertiesGrants() => ShowProperties(GetSelectedGrantPath());

    public void OpenFolderTraverse() => OpenFolder(GetSelectedTraversePath());

    public void CopyPathTraverse() => CopyPath(GetSelectedTraversePath());

    public void ShowPropertiesTraverse() => ShowProperties(GetSelectedTraversePath());

    private void OpenFolder(string? selectedPath)
    {
        if (selectedPath == null)
            return;

        string folder = Directory.Exists(selectedPath)
            ? selectedPath
            : Path.GetDirectoryName(selectedPath) ?? selectedPath;
        shellHelper.OpenInExplorer(folder);
    }

    private static void CopyPath(string? selectedPath)
    {
        if (selectedPath != null)
            Clipboard.SetText(selectedPath);
    }

    private void ShowProperties(string? selectedPath)
    {
        if (selectedPath != null)
            shellHelper.ShowProperties(selectedPath, _owner);
    }

    // --- Grid event routing ---

    public void HandleGrantsRightClickDown(MouseEventArgs e)
        => HandleRightClickDown(e, _controls.GrantsGrid);

    public void HandleTraverseRightClickDown(MouseEventArgs e)
        => HandleRightClickDown(e, _controls.TraverseGrid);

    private static void HandleRightClickDown(MouseEventArgs e, DataGridView grid)
    {
        if (e.Button != MouseButtons.Right) return;
        var hit = grid.HitTest(e.X, e.Y);
        if (hit.RowIndex >= 0)
        {
            var row = grid.Rows[hit.RowIndex];
            if (!row.Selected)
            {
                grid.ClearSelection();
                row.Selected = true;
            }
        }
        else
        {
            grid.ClearSelection();
        }
    }

    public void HandleGrantsMouseClick(MouseEventArgs e)
        => OnGridMouseClick(e, _controls.GrantsGrid);

    public void HandleTraverseMouseClick(MouseEventArgs e)
        => OnGridMouseClick(e, _controls.TraverseGrid);

    private static void OnGridMouseClick(MouseEventArgs e, DataGridView grid)
    {
        if (e.Button == MouseButtons.Left && grid.HitTest(e.X, e.Y).Type == DataGridViewHitTestType.None)
            grid.ClearSelection();
    }
}
