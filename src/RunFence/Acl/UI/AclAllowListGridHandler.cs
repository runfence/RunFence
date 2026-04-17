using RunFence.Core.Models;

namespace RunFence.Acl.UI;

/// <summary>
/// Manages the allow-list grid in <see cref="Forms.AclConfigSection"/>: adds, removes, edits,
/// and validates allow-list entries. Event handlers remain in the form — they extract parameters
/// from UI state and delegate to these public methods.
/// </summary>
public class AclAllowListGridHandler
{
    /// <summary>
    /// Updates the <see cref="AllowAclEntry"/> tag on the given grid row from the current
    /// cell values. Should be called when a cell value changes.
    /// </summary>
    public void ApplyCellValueToEntry(DataGridViewRow row)
    {
        if (row.Tag is AllowAclEntry entry)
        {
            entry.AllowExecute = row.Cells["Execute"].Value is true;
            entry.AllowWrite = row.Cells["Write"].Value is true;
        }
    }

    /// <summary>
    /// Computes context menu visibility for the allow-list context menu based on the current
    /// grid selection. Returns false if the menu should be cancelled entirely (grid disabled).
    /// </summary>
    public bool BuildContextMenuState(bool gridEnabled, bool hasSelection,
        out bool showAdd, out bool showRemove)
    {
        if (!gridEnabled)
        {
            showAdd = false;
            showRemove = false;
            return false;
        }

        showAdd = !hasSelection;
        showRemove = hasSelection;
        return true;
    }

    /// <summary>
    /// Determines the right-click selection state for the allow-list grid at the given hit-test result.
    /// Returns the row index to select, or -1 to clear selection.
    /// </summary>
    public int GetRightClickRowIndex(DataGridView.HitTestInfo hit)
        => hit.RowIndex >= 0 ? hit.RowIndex : -1;
}
