namespace RunFence.Wizard.UI.Forms.Steps;

/// <summary>
/// Specifies when an extra context menu item added via
/// <see cref="FolderListEditor.AddExtraContextMenuItem"/> should be visible.
/// </summary>
public enum ContextMenuTarget
{
    /// <summary>Visible only when right-clicking on empty space (no item selected).</summary>
    EmptySpace,
    /// <summary>Visible only when right-clicking on a selected item.</summary>
    SelectedItem,
}
