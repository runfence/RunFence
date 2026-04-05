using RunFence.UI;

namespace RunFence.Account.UI;

/// <summary>
/// Builds context menu items for the accounts grid and returns an <see cref="AccountContextMenuItems"/> record.
/// All items are created internally; no service dependencies.
/// Called from <see cref="AccountContextMenuOrchestrator.Initialize"/> during panel setup.
/// </summary>
public static class AccountContextMenuBuilder
{
    /// <summary>
    /// Builds all context menu items, adds them to <paramref name="contextMenu"/>,
    /// and returns the populated <see cref="AccountContextMenuItems"/>.
    /// </summary>
    public static AccountContextMenuItems Build(ContextMenuStrip contextMenu)
    {
        var ctxFirewallAllowlist = new ToolStripMenuItem("Internet Whitelist");
        var accountItems = BuildAccountItems(contextMenu);
        var manage = BuildManageSubmenu(contextMenu, ctxFirewallAllowlist);
        var edit = BuildEditSubmenu(contextMenu, accountItems);
        var container = BuildContainerItems(contextMenu);
        var process = BuildProcessItems(contextMenu);

        return new AccountContextMenuItems
        {
            AclManager = manage.AclManager,
            FolderBrowser = manage.FolderBrowser,
            Cmd = manage.Cmd,
            EnvironmentVariables = manage.EnvironmentVariables,
            KillAllProcesses = manage.KillAllProcesses,
            ManageLaunchSeparator = manage.LaunchInstallSeparator,
            EditAccount = accountItems.EditAccount,
            EditCredential = accountItems.EditCredential,
            RemoveCredential = accountItems.RemoveCredential,
            DeleteUser = accountItems.DeleteUser,
            PinFolderBrowserToTray = accountItems.PinFolderBrowserToTray,
            PinDiscoveryToTray = accountItems.PinDiscoveryToTray,
            PinTerminalToTray = accountItems.PinTerminalToTray,
            FirewallAllowlist = ctxFirewallAllowlist,
            CopySid = accountItems.CopySid,
            CopyProfilePath = accountItems.CopyProfilePath,
            OpenProfileFolder = accountItems.OpenProfileFolder,
            CopyPassword = accountItems.CopyPassword,
            TypePassword = accountItems.TypePassword,
            RotatePassword = accountItems.RotatePassword,
            SetEmptyPassword = accountItems.SetEmptyPassword,
            Sep4 = accountItems.Sep4,
            Sep5 = accountItems.Sep5,
            AppsSeparator = accountItems.AppsSeparator,
            NewApp = accountItems.NewApp,
            ManageSeparator = manage.Separator,
            ManageSubmenu = manage.Submenu,
            EditSubmenu = edit.Submenu,
            EditSeparator = edit.Separator,
            InstallItems = manage.InstallItems,
            AddCredential = edit.AddCredential,
            AddCredentialSeparator = edit.AddCredentialSeparator,
            ContainerSeparator = container.Separator,
            CreateContainer = container.CreateContainer,
            EditContainer = container.EditContainer,
            DeleteContainer = container.DeleteContainer,
            CopyContainerProfilePath = container.CopyContainerProfilePath,
            OpenContainerProfileFolder = container.OpenContainerProfileFolder,
            ContainerFolderBrowser = container.ContainerFolderBrowser,
            ProcessSeparator = process.Separator,
            CopyProcessPath = process.CopyProcessPath,
            CopyProcessPid = process.CopyProcessPid,
            CopyProcessArgs = process.CopyProcessArgs,
            CloseProcess = process.CloseProcess,
            KillProcess = process.KillProcess,
            ProcessProperties = process.ProcessProperties
        };
    }

    private static AccountItemsResult BuildAccountItems(ContextMenuStrip contextMenu)
    {
        var ctxEditAccount = new ToolStripMenuItem("Edit Account...")
        {
            Image = UiIconFactory.CreateToolbarIcon("\u270E", Color.FromArgb(0x22, 0x8B, 0x22), 16)
        };

        var ctxEditCredential = new ToolStripMenuItem("Edit Credential...")
        {
            Image = UiIconFactory.CreateToolbarIcon("\u270E", Color.FromArgb(0x33, 0x66, 0x99), 16)
        };

        var ctxRemoveCredential = new ToolStripMenuItem("Remove Credential")
        {
            Image = UiIconFactory.CreateToolbarIcon("\u2212", Color.FromArgb(0xCC, 0x33, 0x33), 16)
        };

        var ctxDeleteUser = new ToolStripMenuItem("Delete Account...")
        {
            Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0xCC, 0x33, 0x33), 16)
        };

        var ctxPinFolderBrowserToTray = new ToolStripMenuItem("Pin Folder Browser to Tray");
        var ctxPinDiscoveryToTray = new ToolStripMenuItem("Pin App Discovery to Tray");
        var ctxPinTerminalToTray = new ToolStripMenuItem("Pin Terminal to Tray");

        var ctxCopySid = new ToolStripMenuItem("Copy SID");
        var ctxCopyProfilePath = new ToolStripMenuItem("Copy Profile Path");
        var ctxOpenProfileFolder = new ToolStripMenuItem("Open Profile Folder");
        var ctxCopyPassword = new ToolStripMenuItem("Copy Password");
        var ctxTypePassword = new ToolStripMenuItem("Type Password");
        var ctxRotatePassword = new ToolStripMenuItem("Rotate Account Password");
        var ctxSetEmptyPassword = new ToolStripMenuItem("Set Empty Account Password");

        var ctxSep4 = new ToolStripSeparator();
        var ctxSep5 = new ToolStripSeparator();
        var ctxAppsSeparator = new ToolStripSeparator();
        var ctxNewApp = new ToolStripMenuItem("New App...");

        // Add account items to context menu (Edit submenu and Manage submenu are inserted separately)
        contextMenu.Items.AddRange(ctxCopyPassword, ctxTypePassword, ctxSep4, ctxPinFolderBrowserToTray, ctxPinDiscoveryToTray, ctxPinTerminalToTray, ctxSep5, ctxCopySid, ctxCopyProfilePath, ctxOpenProfileFolder, ctxAppsSeparator, ctxNewApp);

        return new AccountItemsResult(
            ctxEditAccount, ctxEditCredential, ctxRemoveCredential, ctxDeleteUser,
            ctxPinFolderBrowserToTray, ctxPinDiscoveryToTray, ctxPinTerminalToTray,
            ctxCopySid, ctxCopyProfilePath, ctxOpenProfileFolder,
            ctxCopyPassword, ctxTypePassword, ctxRotatePassword, ctxSetEmptyPassword,
            new ToolStripSeparator(), new ToolStripSeparator(), new ToolStripSeparator(),
            ctxSep4, ctxSep5, ctxAppsSeparator, ctxNewApp);
    }

    private static ManageSubmenuResult BuildManageSubmenu(ContextMenuStrip contextMenu, ToolStripMenuItem ctxFirewallAllowlist)
    {
        var ctxManageSeparator = new ToolStripSeparator();
        var ctxManageSubmenu = new ToolStripMenuItem("Manage");

        var ctxMngAclManager = new ToolStripMenuItem("ACL Manager");
        ctxMngAclManager.Image = UiIconFactory.CreateToolbarIcon("\U0001F4DC", Color.FromArgb(0xCC, 0x99, 0x00), 16);

        var ctxMngFolderBrowser = new ToolStripMenuItem("Folder Browser");
        ctxMngFolderBrowser.Image = UiIconFactory.CreateToolbarIcon("\U0001F4C2", Color.FromArgb(0xCC, 0x88, 0x22), 16);

        var ctxMngCmd = new ToolStripMenuItem("CMD");
        ctxMngCmd.Image = UiIconFactory.CreateToolbarIcon(">", Color.FromArgb(0x33, 0x33, 0x33), 16);

        var ctxMngEnvVars = new ToolStripMenuItem("Environment Variables");
        ctxMngEnvVars.Image = UiIconFactory.CreateToolbarIcon("%", Color.FromArgb(0x33, 0x66, 0x99), 16);

        var ctxMngKillAllProcesses = new ToolStripMenuItem("Kill All Processes");
        ctxMngKillAllProcesses.Image = UiIconFactory.CreateToolbarIcon("\u2716", Color.FromArgb(0xCC, 0x22, 0x22), 16);

        var ctxMngLaunchInstallSeparator = new ToolStripSeparator();

        ctxManageSubmenu.DropDownItems.Add(ctxMngAclManager);
        ctxManageSubmenu.DropDownItems.Add(ctxMngFolderBrowser);
        ctxManageSubmenu.DropDownItems.Add(ctxMngCmd);
        ctxManageSubmenu.DropDownItems.Add(ctxMngEnvVars);
        ctxManageSubmenu.DropDownItems.Add(ctxFirewallAllowlist);
        ctxManageSubmenu.DropDownItems.Add(ctxMngKillAllProcesses);
        ctxManageSubmenu.DropDownItems.Add(ctxMngLaunchInstallSeparator);

        var installItems = new List<(InstallablePackage Package, ToolStripMenuItem Item)>();
        foreach (var package in KnownPackages.All)
        {
            var item = new ToolStripMenuItem($"Install {package.DisplayName}");
            ctxManageSubmenu.DropDownItems.Add(item);
            installItems.Add((package, item));
        }

        contextMenu.Items.Insert(0, ctxManageSeparator);
        contextMenu.Items.Insert(0, ctxManageSubmenu);

        return new ManageSubmenuResult(ctxManageSubmenu, ctxManageSeparator, ctxMngAclManager,
            ctxMngFolderBrowser, ctxMngCmd, ctxMngEnvVars, ctxMngKillAllProcesses,
            ctxMngLaunchInstallSeparator, installItems.AsReadOnly());
    }

    private static EditSubmenuResult BuildEditSubmenu(ContextMenuStrip contextMenu, AccountItemsResult items)
    {
        var ctxEditSeparator = new ToolStripSeparator();
        var ctxEditSubmenu = new ToolStripMenuItem("Edit");
        ctxEditSubmenu.DropDownItems.Add(items.EditAccount);
        ctxEditSubmenu.DropDownItems.Add(items.EditCredential);
        ctxEditSubmenu.DropDownItems.Add(items.Sep1);
        ctxEditSubmenu.DropDownItems.Add(items.RotatePassword);
        ctxEditSubmenu.DropDownItems.Add(items.SetEmptyPassword);
        ctxEditSubmenu.DropDownItems.Add(items.Sep2);
        ctxEditSubmenu.DropDownItems.Add(items.RemoveCredential);
        ctxEditSubmenu.DropDownItems.Add(items.Sep3);
        ctxEditSubmenu.DropDownItems.Add(items.DeleteUser);
        // Insert after [Manage(0), ManageSeparator(1)] -> [Manage, ManageSeparator, Edit, EditSeparator, ...]
        contextMenu.Items.Insert(2, ctxEditSeparator);
        contextMenu.Items.Insert(2, ctxEditSubmenu);

        // Add Credential shortcut: shown at top of context menu for accounts without a credential.
        // Hidden by default; visibility toggled in ShowAccountMenu based on credential state.
        var ctxAddCredential = new ToolStripMenuItem("Add Credential");
        ctxAddCredential.Image = UiIconFactory.CreateToolbarIcon("\U0001F511", Color.FromArgb(0x22, 0x8B, 0x22), 16);
        var ctxAddCredentialSeparator = new ToolStripSeparator();
        contextMenu.Items.Insert(0, ctxAddCredentialSeparator);
        contextMenu.Items.Insert(0, ctxAddCredential);

        contextMenu.ShowItemToolTips = true;

        return new EditSubmenuResult(ctxEditSubmenu, ctxEditSeparator, ctxAddCredential, ctxAddCredentialSeparator);
    }

    private static ContainerItemsResult BuildContainerItems(ContextMenuStrip contextMenu)
    {
        var ctxContainerSeparator = new ToolStripSeparator();
        var ctxCreateContainer = new ToolStripMenuItem("Create Container...");
        var ctxEditContainer = new ToolStripMenuItem("Edit Container...");
        var ctxDeleteContainer = new ToolStripMenuItem("Delete Container...");
        var ctxCopyContainerProfilePath = new ToolStripMenuItem("Copy Container Profile Path");
        var ctxOpenContainerProfileFolder = new ToolStripMenuItem("Open Container Profile Folder");
        var ctxContainerFolderBrowser = new ToolStripMenuItem("Launch Folder Browser");

        contextMenu.Items.Add(ctxContainerSeparator);
        contextMenu.Items.Add(ctxCreateContainer);
        contextMenu.Items.Add(ctxEditContainer);
        contextMenu.Items.Add(ctxDeleteContainer);
        contextMenu.Items.Add(ctxCopyContainerProfilePath);
        contextMenu.Items.Add(ctxOpenContainerProfileFolder);
        contextMenu.Items.Add(ctxContainerFolderBrowser);

        return new ContainerItemsResult(ctxContainerSeparator, ctxCreateContainer, ctxEditContainer,
            ctxDeleteContainer, ctxCopyContainerProfilePath, ctxOpenContainerProfileFolder,
            ctxContainerFolderBrowser);
    }

    private static ProcessItemsResult BuildProcessItems(ContextMenuStrip contextMenu)
    {
        var ctxProcessSeparator = new ToolStripSeparator();
        var ctxCopyProcessPath = new ToolStripMenuItem("Copy Path");
        ctxCopyProcessPath.Image = UiIconFactory.CreateClipboardIcon();
        var ctxCopyProcessPid = new ToolStripMenuItem("Copy PID");
        ctxCopyProcessPid.Image = UiIconFactory.CreateClipboardIcon();
        var ctxCopyProcessArgs = new ToolStripMenuItem("Copy Args");
        ctxCopyProcessArgs.Image = UiIconFactory.CreateClipboardIcon();
        var ctxCloseProcess = new ToolStripMenuItem("Close");
        ctxCloseProcess.Image = UiIconFactory.CreateToolbarIcon("\u2715", Color.FromArgb(0x88, 0x66, 0x22), 16);
        var ctxKillProcess = new ToolStripMenuItem("Kill Process...");
        ctxKillProcess.Image = UiIconFactory.CreateToolbarIcon("\u2716", Color.FromArgb(0xCC, 0x22, 0x22), 16);
        var ctxProcessProperties = new ToolStripMenuItem("Properties");
        ctxProcessProperties.Image = UiIconFactory.CreatePropertiesIcon();
        contextMenu.Items.Add(ctxProcessSeparator);
        contextMenu.Items.Add(ctxCopyProcessPath);
        contextMenu.Items.Add(ctxCopyProcessPid);
        contextMenu.Items.Add(ctxCopyProcessArgs);
        contextMenu.Items.Add(ctxCloseProcess);
        contextMenu.Items.Add(ctxKillProcess);
        contextMenu.Items.Add(ctxProcessProperties);

        return new ProcessItemsResult(ctxProcessSeparator, ctxCopyProcessPath, ctxCopyProcessPid,
            ctxCopyProcessArgs, ctxCloseProcess, ctxKillProcess, ctxProcessProperties);
    }

    private record AccountItemsResult(
        ToolStripMenuItem EditAccount,
        ToolStripMenuItem EditCredential,
        ToolStripMenuItem RemoveCredential,
        ToolStripMenuItem DeleteUser,
        ToolStripMenuItem PinFolderBrowserToTray,
        ToolStripMenuItem PinDiscoveryToTray,
        ToolStripMenuItem PinTerminalToTray,
        ToolStripMenuItem CopySid,
        ToolStripMenuItem CopyProfilePath,
        ToolStripMenuItem OpenProfileFolder,
        ToolStripMenuItem CopyPassword,
        ToolStripMenuItem TypePassword,
        ToolStripMenuItem RotatePassword,
        ToolStripMenuItem SetEmptyPassword,
        ToolStripSeparator Sep1,
        ToolStripSeparator Sep2,
        ToolStripSeparator Sep3,
        ToolStripSeparator Sep4,
        ToolStripSeparator Sep5,
        ToolStripSeparator AppsSeparator,
        ToolStripMenuItem NewApp);

    private record ManageSubmenuResult(
        ToolStripMenuItem Submenu,
        ToolStripSeparator Separator,
        ToolStripMenuItem AclManager,
        ToolStripMenuItem FolderBrowser,
        ToolStripMenuItem Cmd,
        ToolStripMenuItem EnvironmentVariables,
        ToolStripMenuItem KillAllProcesses,
        ToolStripSeparator LaunchInstallSeparator,
        IReadOnlyList<(InstallablePackage Package, ToolStripMenuItem Item)> InstallItems);

    private record EditSubmenuResult(
        ToolStripMenuItem Submenu,
        ToolStripSeparator Separator,
        ToolStripMenuItem AddCredential,
        ToolStripSeparator AddCredentialSeparator);

    private record ContainerItemsResult(
        ToolStripSeparator Separator,
        ToolStripMenuItem CreateContainer,
        ToolStripMenuItem EditContainer,
        ToolStripMenuItem DeleteContainer,
        ToolStripMenuItem CopyContainerProfilePath,
        ToolStripMenuItem OpenContainerProfileFolder,
        ToolStripMenuItem ContainerFolderBrowser);

    private record ProcessItemsResult(
        ToolStripSeparator Separator,
        ToolStripMenuItem CopyProcessPath,
        ToolStripMenuItem CopyProcessPid,
        ToolStripMenuItem CopyProcessArgs,
        ToolStripMenuItem CloseProcess,
        ToolStripMenuItem KillProcess,
        ToolStripMenuItem ProcessProperties);
}