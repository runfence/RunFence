using RunFence.Acl.UI.Forms;
using RunFence.Persistence;

namespace RunFence.Acl.UI;

/// <summary>
/// Handles mouse drag-drop reordering and shell file-drop events for the grants and traverse
/// grids of <see cref="AclManagerDialog"/>. Delegates drag logic to <see cref="AclManagerDragDropHandler"/>
/// and shell-drop logic to <see cref="AclManagerActionOrchestrator"/>.
/// </summary>
public class AclManagerMouseEventHandler(
    IAppConfigService appConfigService,
    AclManagerDragDropHandler dragDropHandler,
    AclManagerActionOrchestrator actionHandler)
{
    private AclManagerDialogControls _controls = null!;
    private Action _refreshGrantsGrid = null!;
    private Action _refreshTraverseGrid = null!;
    private Action _updateActionButtons = null!;

    public void Initialize(
        AclManagerDialogControls controls,
        Action refreshGrantsGrid,
        Action refreshTraverseGrid,
        Action updateActionButtons)
    {
        _controls = controls;
        _refreshGrantsGrid = refreshGrantsGrid;
        _refreshTraverseGrid = refreshTraverseGrid;
        _updateActionButtons = updateActionButtons;
    }

    // --- Mouse drag ---

    public void HandleGrantsMouseDown(MouseEventArgs e)
        => dragDropHandler.HandleMouseDown(e, _controls.GrantsGrid);

    public void HandleGrantsMouseMove(MouseEventArgs e)
        => dragDropHandler.HandleMouseMove(e, _controls.GrantsGrid);

    public void HandleTraverseMouseDown(MouseEventArgs e)
        => dragDropHandler.HandleMouseDown(e, _controls.TraverseGrid);

    public void HandleTraverseMouseMove(MouseEventArgs e)
        => dragDropHandler.HandleMouseMove(e, _controls.TraverseGrid);

    public void HandleGrantsMouseUp(MouseEventArgs e)
    {
        if (dragDropHandler.HandleMouseUp(e, _controls.GrantsGrid))
        {
            _refreshGrantsGrid();
            _updateActionButtons();
        }
    }

    public void HandleTraverseMouseUp(MouseEventArgs e)
    {
        if (dragDropHandler.HandleMouseUp(e, _controls.TraverseGrid))
        {
            _refreshTraverseGrid();
            _updateActionButtons();
        }
    }

    // --- Shell file drop ---

    public void HandleGrantsFileDrop(string[] paths)
    {
        string? targetConfigPath = null;
        bool hasExplicitTargetSection = false;
        if (appConfigService.HasLoadedConfigs)
        {
            var cursorClient = _controls.GrantsGrid.PointToClient(Cursor.Position);
            var hitTest = _controls.GrantsGrid.HitTest(cursorClient.X, cursorClient.Y);
            if (hitTest.RowIndex >= 0)
            {
                hasExplicitTargetSection = true;
                targetConfigPath = AclManagerSectionHeader.GetSectionConfigPath(_controls.GrantsGrid, hitTest.RowIndex);
            }
        }

        var error = actionHandler.HandleShellDropOnGrants(paths, targetConfigPath, hasExplicitTargetSection);
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
            actionHandler.HandleShellDropOnTraverse(paths);
        _updateActionButtons();
    }
}
