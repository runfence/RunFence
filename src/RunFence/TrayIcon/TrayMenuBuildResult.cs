namespace RunFence.TrayIcon;

public sealed record TrayMenuBuildResult(
    ContextMenuStrip Menu,
    ToolStripMenuItem ShowItem,
    ToolStripMenuItem LockItem);
