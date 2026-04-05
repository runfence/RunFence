using RunFence.Acl.UI.Forms;
using RunFence.UI;

namespace RunFence.Acl.UI;

/// <summary>
/// Static setup helpers for <see cref="AclManagerDialog"/> toolbar and grid visual configuration.
/// Extracted to keep the dialog class focused on behavior.
/// </summary>
public static class AclManagerDialogUiSetup
{
    public static void ConfigureToolbar(
        ToolStrip toolStrip,
        ToolStripButton addFile,
        ToolStripButton addFolder,
        ToolStripButton scan,
        ToolStripButton remove,
        ToolStripButton fix,
        ToolStripButton export,
        ToolStripButton import)
    {
        toolStrip.ImageScalingSize = new Size(30, 30);
        addFile.DisplayStyle = ToolStripItemDisplayStyle.Image;
        addFile.Image = UiIconFactory.CreateToolbarIcon("\U0001F4C4", Color.FromArgb(0xA0, 0xA0, 0xA0), 30);
        addFile.ToolTipText = "Add File";
        addFolder.DisplayStyle = ToolStripItemDisplayStyle.Image;
        addFolder.Image = UiIconFactory.CreateToolbarIcon("\U0001F4C1", Color.FromArgb(0xFF, 0xC0, 0x00), 30);
        addFolder.ToolTipText = "Add Folder";
        scan.DisplayStyle = ToolStripItemDisplayStyle.Image;
        scan.Image = UiIconFactory.CreateToolbarIcon("\U0001F50D", Color.FromArgb(0x33, 0x66, 0x99), 30);
        scan.ToolTipText = "Scan Folder";
        remove.DisplayStyle = ToolStripItemDisplayStyle.Image;
        remove.Image = UiIconFactory.CreateToolbarIcon("\u2212", Color.FromArgb(0xCC, 0x33, 0x33), 30);
        remove.ToolTipText = "Delete";
        fix.DisplayStyle = ToolStripItemDisplayStyle.Image;
        fix.Image = UiIconFactory.CreateToolbarIcon("\U0001F527", Color.FromArgb(0x99, 0x66, 0x00), 30);
        fix.ToolTipText = "Fix ACLs";
        export.DisplayStyle = ToolStripItemDisplayStyle.Image;
        export.Image = UiIconFactory.CreateToolbarIcon("\u2191", Color.FromArgb(0x33, 0x66, 0x99), 30);
        export.ToolTipText = "Export";
        import.DisplayStyle = ToolStripItemDisplayStyle.Image;
        import.Image = UiIconFactory.CreateToolbarIcon("\u21A9", Color.FromArgb(0x66, 0x66, 0x99), 30);
        import.ToolTipText = "Import";
    }
}