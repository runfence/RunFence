using System.ComponentModel;
using RunFence.Acl.UI;
using RunFence.Core.Models;
using RunFence.Launch;

namespace RunFence.Account.UI;

/// <summary>
/// Coordinates context menu visibility and action routing for the accounts grid.
/// Delegates to <see cref="AccountContextMenuHandler"/> (account rows),
/// <see cref="ContainerContextMenuHandler"/> (container rows),
/// <see cref="AccountFirewallMenuHandler"/> (firewall dialog),
/// and <see cref="AccountProcessMenuHandler"/> (process rows).
/// Builds its own <see cref="AccountContextMenuItems"/> on <see cref="Initialize"/>.
/// </summary>
public class AccountContextMenuOrchestrator
{
    private readonly AccountContextMenuHandler _accountHandler;
    private readonly ContainerContextMenuHandler _containerHandler;
    private readonly AccountFirewallMenuHandler _firewallHandler;
    private readonly AccountProcessMenuHandler _processMenuHandler;
    private readonly ToolLauncher _launchService;
    private readonly AccountTrayToggleService _trayToggleService;
    private readonly ISidNameCacheService _sidNameCache;

    private DataGridView _grid = null!;
    private ContextMenuStrip _contextMenu = null!;

    public event Action<string>? AppNavigationRequested;
    public event Action<string>? NewAppRequested;
    public event Action? DataChangedAndRefresh;
    public event EventHandler? EditCredentialRequested;

    public AccountContextMenuOrchestrator(
        AccountContextMenuHandler accountHandler,
        ContainerContextMenuHandler containerHandler,
        AccountFirewallMenuHandler firewallHandler,
        AccountProcessMenuHandler processMenuHandler,
        ToolLauncher launchService,
        AccountTrayToggleService trayToggleService,
        ISidNameCacheService sidNameCache)
    {
        _accountHandler = accountHandler;
        _containerHandler = containerHandler;
        _firewallHandler = firewallHandler;
        _processMenuHandler = processMenuHandler;
        _launchService = launchService;
        _trayToggleService = trayToggleService;
        _sidNameCache = sidNameCache;

        _accountHandler.AppNavigationRequested += sid => AppNavigationRequested?.Invoke(sid);
        _accountHandler.NewAppRequested += sid => NewAppRequested?.Invoke(sid);
        _containerHandler.DataChangedAndRefresh += () => DataChangedAndRefresh?.Invoke();
        _firewallHandler.SaveAndRefreshRequested += () => DataChangedAndRefresh?.Invoke();
    }

    public void Initialize(DataGridView grid, ContextMenuStrip contextMenu,
        IAccountsPanelContext panelContext,
        ToolStripMenuItem hdrCreateContainer, AccountProcessDisplayManager? processDisplayManager = null)
    {
        _grid = grid;
        _contextMenu = contextMenu;
        Items = AccountContextMenuBuilder.Build(contextMenu);

        _processMenuHandler.Initialize(grid, Items, panelContext);
        _containerHandler.Initialize(grid);
        _firewallHandler.Initialize(grid);
        _accountHandler.Initialize(grid, processDisplayManager);
        WireItemClickEvents(hdrCreateContainer);
    }

    public AccountContextMenuItems Items { get; private set; } = null!;

    public bool IsFirewallAvailable => _firewallHandler.IsAvailable;

    public bool IsWindowsTerminal(string sid) => _launchService.IsWindowsTerminal(sid);

    private void WireItemClickEvents(ToolStripMenuItem hdrCreateContainer)
    {
        Items.AclManager.Click += (_, _) => OpenAclManager();
        Items.FolderBrowser.Click += (_, _) => OpenFolderBrowser();
        Items.Cmd.Click += (_, _) => OpenCmd();
        Items.EnvironmentVariables.Click += (_, _) => OpenEnvironmentVariables();
        Items.KillAllProcesses.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } ar)
                _accountHandler.KillAllProcesses(ar);
        };
        Items.NewApp.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } ar)
                _accountHandler.RequestNewApp(ar.Sid);
        };
        Items.AddCredential.Click += (s, e) => EditCredentialRequested?.Invoke(s, e);
        Items.FirewallAllowlist.Click += (_, _) =>
        {
            if (GetSelectedAccountRow() is { } ar)
                _firewallHandler.OpenFirewallAllowlist(ar);
        };

        Items.CreateContainer.Click += (_, _) => _containerHandler.CreateContainer();
        hdrCreateContainer.Click += (_, _) => _containerHandler.CreateContainer();
        Items.EditContainer.Click += (_, _) =>
        {
            if (GetSelectedContainerRow() is { } cr)
                _containerHandler.EditContainer(cr);
        };
        Items.DeleteContainer.Click += (_, _) =>
        {
            if (GetSelectedContainerRow() is { } cr)
                _containerHandler.DeleteContainer(cr);
        };
        Items.CopyContainerProfilePath.Click += (_, _) =>
        {
            if (GetSelectedContainerRow() is { } cr)
                _containerHandler.CopyContainerProfilePath(cr);
        };
        Items.OpenContainerProfileFolder.Click += (_, _) =>
        {
            if (GetSelectedContainerRow() is { } cr)
                _containerHandler.OpenContainerProfileFolder(cr);
        };
        Items.ContainerFolderBrowser.Click += (_, _) => OpenFolderBrowser();

        foreach (var (package, item) in Items.InstallItems)
        {
            var capturedPackage = package;
            item.Click += (_, _) => HandleInstallRequest(capturedPackage);
        }
    }

    public void OpenCmd()
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        if (_grid.SelectedRows[0].Tag is ContainerRow containerRow)
        {
            _launchService.OpenCmd(new AppContainerLaunchIdentity(containerRow.Container));
            return;
        }
        if (_grid.SelectedRows[0].Tag is not AccountRow accountRow)
            return;
        var shift = (Control.ModifierKeys & Keys.Shift) != 0;
        _launchService.OpenCmd(new AccountLaunchIdentity(accountRow.Sid)
            { PrivilegeLevel = shift ? PrivilegeLevel.HighestAllowed : null });
    }

    public void OpenFolderBrowser()
    {
        if (_grid.SelectedRows.Count == 0)
            return;
        var permissionPrompt = AclPermissionDialogHelper.CreateLaunchPermissionPrompt(_sidNameCache, _grid.FindForm());
        if (_grid.SelectedRows[0].Tag is ContainerRow containerRow)
        {
            _launchService.OpenFolderBrowser(new AppContainerLaunchIdentity(containerRow.Container), permissionPrompt);
            return;
        }
        if (_grid.SelectedRows[0].Tag is not AccountRow accountRow)
            return;
        var shift = (Control.ModifierKeys & Keys.Shift) != 0;
        _launchService.OpenFolderBrowser(new AccountLaunchIdentity(accountRow.Sid)
            { PrivilegeLevel = shift ? PrivilegeLevel.HighestAllowed : null }, permissionPrompt);
    }

    public void OpenEnvironmentVariables()
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not AccountRow accountRow)
            return;
        var shift = (Control.ModifierKeys & Keys.Shift) != 0;
        _launchService.OpenEnvironmentVariables(new AccountLaunchIdentity(accountRow.Sid)
            { PrivilegeLevel = shift ? PrivilegeLevel.HighestAllowed : null });
    }

    public void HandleInstallRequest(InstallablePackage package)
    {
        if (GetSelectedAccountRow() is not { } ar)
            return;
        var identity = new AccountLaunchIdentity(ar.Sid);
        if (package == KnownPackages.WindowsTerminal && !_launchService.IsPackageInstalled(KnownPackages.Winget, ar.Sid))
            _launchService.InstallPackages([KnownPackages.Winget, package], identity);
        else
            _launchService.InstallPackage(package, identity);
    }

    public void ToggleFolderBrowserTray()
    {
        if (GetSelectedAccountRow() is not { } ar || string.IsNullOrEmpty(ar.Sid))
            return;
        _trayToggleService.ToggleFolderBrowserTray(ar.Sid, () => DataChangedAndRefresh?.Invoke());
    }

    public void ToggleDiscoveryTray()
    {
        if (GetSelectedAccountRow() is not { } ar || string.IsNullOrEmpty(ar.Sid))
            return;
        _trayToggleService.ToggleDiscoveryTray(ar.Sid, () => DataChangedAndRefresh?.Invoke());
    }

    public void ToggleTerminalTray()
    {
        if (GetSelectedAccountRow() is not { } ar || string.IsNullOrEmpty(ar.Sid))
            return;
        _trayToggleService.ToggleTerminalTray(ar.Sid, () => DataChangedAndRefresh?.Invoke());
    }

    public void ToggleManageAssociations()
    {
        if (GetSelectedAccountRow() is not { } ar || string.IsNullOrEmpty(ar.Sid))
            return;
        _trayToggleService.ToggleManageAssociations(ar.Sid, () => DataChangedAndRefresh?.Invoke());
    }

    private AccountRow? GetSelectedAccountRow()
        => AccountGridHelper.GetSelectedAccountRow(_grid);

    private ContainerRow? GetSelectedContainerRow()
        => _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag as ContainerRow : null;

    public void HandleContextMenuOpening(CancelEventArgs e)
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not IAccountGridRow gridRow)
        {
            e.Cancel = true;
            return;
        }

        if (_grid.SelectedRows.Count > 1)
        {
            var firstType = _grid.SelectedRows[0].Tag?.GetType();
            bool mixed = _grid.SelectedRows.Cast<DataGridViewRow>()
                .Any(r => r.Tag?.GetType() != firstType);
            if (mixed)
            {
                e.Cancel = true;
                return;
            }
        }

        // Clear dynamic app items
        foreach (var item in Items.DynamicAppItems)
        {
            _contextMenu.Items.Remove(item);
            item.Dispose();
        }

        Items.DynamicAppItems.Clear();

        switch (gridRow)
        {
            case ProcessRow processRow:
                ShowProcessMenu(processRow);
                return;
            case ContainerRow containerRow:
                _accountHandler.ShowContainerMenu(containerRow, Items);
                return;
        }

        if (gridRow is not AccountRow accountRow)
        {
            e.Cancel = true;
            return;
        }

        _accountHandler.ShowAccountMenu(accountRow, Items, _contextMenu);
    }

    private void ShowProcessMenu(ProcessRow processRow)
    {
        var i = Items;
        AccountContextMenuHandler.SetAccountItemsVisible(i, false);
        i.FirewallAllowlist.Visible = false;
        i.AppsSeparator.Visible = false;
        i.NewApp.Visible = false;
        AccountContextMenuHandler.SetContainerItemsVisible(i, false);

        _processMenuHandler.ShowProcessMenu(processRow);
    }

    public void OpenAclManager()
    {
        if (GetSelectedContainerRow() is { } containerRow)
            _containerHandler.OpenAclManager(containerRow);
        else if (GetSelectedAccountRow() is { } accountRow)
            _containerHandler.OpenAclManager(accountRow);
    }

    public void TriggerCloseSelectedProcess()
        => _processMenuHandler.TriggerCloseProcess();

    public void OpenFirewallAllowlist()
    {
        if (GetSelectedAccountRow() is { } accountRow)
            _firewallHandler.OpenFirewallAllowlist(accountRow);
    }
}