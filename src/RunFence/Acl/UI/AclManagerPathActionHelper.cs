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
    private void OpenInExplorer(string path)
    {
        string folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
        shellHelper.OpenInExplorer(folder);
    }

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

    public void OpenFolderGrants()
        => OpenInExplorer(GetSelectedGrantPath()!);

    public void CopyPathGrants()
        => Clipboard.SetText(GetSelectedGrantPath()!);

    public void ShowPropertiesGrants()
        => shellHelper.ShowProperties(GetSelectedGrantPath()!, _owner);

    public void OpenFolderTraverse()
        => OpenInExplorer(GetSelectedTraversePath()!);

    public void CopyPathTraverse()
        => Clipboard.SetText(GetSelectedTraversePath()!);

    public void ShowPropertiesTraverse()
        => shellHelper.ShowProperties(GetSelectedTraversePath()!, _owner);

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
