namespace RunFence.Account.UI;

/// <summary>
/// Groups all context menu item references used by <see cref="AccountContextMenuOrchestrator"/>
/// and its sub-handlers. All items are built internally by <see cref="AccountContextMenuOrchestrator.Initialize"/>.
/// </summary>
public class AccountContextMenuItems
{
    /// <summary>
    /// Dynamically inserted app navigation items (inserted before <see cref="NewApp"/>).
    /// Cleared and repopulated each time the context menu opens for an account row.
    /// </summary>
    public List<ToolStripMenuItem> DynamicAppItems { get; } = new();

    // Launch tools inside Manage submenu (shared by accounts and containers)
    public required ToolStripMenuItem AclManager { get; init; }
    public required ToolStripMenuItem FolderBrowser { get; init; }

    public required ToolStripMenuItem Cmd { get; init; }

    // Account-only Manage submenu items (hidden for containers)
    public required ToolStripMenuItem EnvironmentVariables { get; init; }
    public required ToolStripMenuItem KillAllProcesses { get; init; }
    public required ToolStripSeparator ManageLaunchSeparator { get; init; }

    // Account items (created in AccountContextMenuBuilder)
    public required ToolStripMenuItem EditAccount { get; init; }
    public required ToolStripMenuItem EditCredential { get; init; }
    public required ToolStripMenuItem RemoveCredential { get; init; }
    public required ToolStripMenuItem DeleteUser { get; init; }
    public required ToolStripMenuItem PinFolderBrowserToTray { get; init; }
    public required ToolStripMenuItem PinDiscoveryToTray { get; init; }
    public required ToolStripMenuItem PinTerminalToTray { get; init; }
    public required ToolStripMenuItem ManageAssociations { get; init; }
    public required ToolStripMenuItem CopySid { get; init; }
    public required ToolStripMenuItem CopyProfilePath { get; init; }
    public required ToolStripMenuItem OpenProfileFolder { get; init; }
    public required ToolStripMenuItem CopyPassword { get; init; }
    public required ToolStripMenuItem TypePassword { get; init; }
    public required ToolStripMenuItem RotatePassword { get; init; }
    public required ToolStripMenuItem SetEmptyPassword { get; init; }
    public required ToolStripSeparator Sep4 { get; init; }
    public required ToolStripSeparator Sep5 { get; init; }
    public required ToolStripSeparator AppsSeparator { get; init; }
    public required ToolStripMenuItem NewApp { get; init; }

    // Manage submenu (created in code)
    public required ToolStripSeparator ManageSeparator { get; init; }
    public required ToolStripMenuItem ManageSubmenu { get; init; }

    // Edit submenu (created in code)
    public required ToolStripMenuItem EditSubmenu { get; init; }
    public required ToolStripSeparator EditSeparator { get; init; }
    public required IReadOnlyList<(InstallablePackage Package, ToolStripMenuItem Item)> InstallItems { get; init; }

    // Add Credential shortcut (top-level, shown instead of EditCredential when account has no credential)
    public required ToolStripMenuItem AddCredential { get; init; }
    public required ToolStripSeparator AddCredentialSeparator { get; init; }

    // Internet whitelist (created in AccountContextMenuBuilder)
    public required ToolStripMenuItem FirewallAllowlist { get; init; }

    // Container items (created in code)
    public required ToolStripSeparator ContainerSeparator { get; init; }
    public required ToolStripMenuItem CreateContainer { get; init; }
    public required ToolStripMenuItem EditContainer { get; init; }
    public required ToolStripMenuItem DeleteContainer { get; init; }
    public required ToolStripMenuItem CopyContainerProfilePath { get; init; }
    public required ToolStripMenuItem OpenContainerProfileFolder { get; init; }
    public required ToolStripMenuItem ContainerFolderBrowser { get; init; }

    // Process row items (created in code)
    public required ToolStripSeparator ProcessSeparator { get; init; }
    public required ToolStripMenuItem CopyProcessPath { get; init; }
    public required ToolStripMenuItem CopyProcessPid { get; init; }
    public required ToolStripMenuItem CopyProcessArgs { get; init; }
    public required ToolStripMenuItem CloseProcess { get; init; }
    public required ToolStripMenuItem KillProcess { get; init; }
    public required ToolStripMenuItem ProcessProperties { get; init; }
}