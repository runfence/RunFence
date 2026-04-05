using RunFence.Acl.UI.Forms;

namespace RunFence.Acl.UI;

/// <summary>
/// Bundles control references from <see cref="AclManagerDialog"/> for passing to
/// <see cref="AclManagerSelectionHandler"/> and <see cref="AclManagerModificationHandler"/>
/// without exposing individual dialog fields.
/// </summary>
public class AclManagerDialogControls
{
    public required TabControl TabControl { get; init; }
    public required TabPage TraverseTab { get; init; }
    public required DataGridView GrantsGrid { get; init; }
    public required DataGridView TraverseGrid { get; init; }
    public required ToolStripButton AddFileButton { get; init; }
    public required ToolStripButton AddFolderButton { get; init; }
    public required ToolStripButton RemoveButton { get; init; }
    public required ToolStripButton FixAclsButton { get; init; }
    public required Button ApplyButton { get; init; }
    public required ToolStripLabel ScanStatusLabel { get; init; }
    public required ToolStripProgressBar ProgressBar { get; init; }

    // Grants context menu
    public required ToolStripMenuItem CtxAddFile { get; init; }
    public required ToolStripMenuItem CtxAddFolder { get; init; }
    public required ToolStripSeparator CtxGrantsSep { get; init; }
    public required ToolStripMenuItem CtxRemove { get; init; }
    public required ToolStripMenuItem CtxUntrack { get; init; }
    public required ToolStripMenuItem CtxFixAcls { get; init; }
    public required ToolStripSeparator CtxGrantsOpenFolderSep { get; init; }
    public required ToolStripMenuItem CtxOpenFolderGrants { get; init; }
    public required ToolStripMenuItem CtxCopyPathGrants { get; init; }
    public required ToolStripSeparator CtxGrantsPropertiesSep { get; init; }
    public required ToolStripMenuItem CtxPropertiesGrants { get; init; }

    // Traverse context menu
    public required ToolStripMenuItem CtxTraverseAddFile { get; init; }
    public required ToolStripMenuItem CtxTraverseAddFolder { get; init; }
    public required ToolStripSeparator CtxTraverseSep { get; init; }
    public required ToolStripMenuItem CtxTraverseRemove { get; init; }
    public required ToolStripMenuItem CtxTraverseUntrack { get; init; }
    public required ToolStripMenuItem CtxTraverseFixAcls { get; init; }
    public required ToolStripSeparator CtxTraverseOpenFolderSep { get; init; }
    public required ToolStripMenuItem CtxTraverseOpenFolder { get; init; }
    public required ToolStripMenuItem CtxTraverseCopyPath { get; init; }
    public required ToolStripSeparator CtxTraversePropertiesSep { get; init; }
    public required ToolStripMenuItem CtxTraverseProperties { get; init; }
}