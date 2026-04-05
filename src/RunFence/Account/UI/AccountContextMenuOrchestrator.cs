using System.ComponentModel;

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

    private DataGridView _grid = null!;
    private ContextMenuStrip _contextMenu = null!;

    public event Action<string>? AppNavigationRequested;
    public event Action<string>? NewAppRequested;
    public event Action? DataChangedAndRefresh;
    public event Action<InstallablePackage>? InstallRequested;
    public event EventHandler? OpenFolderBrowserRequested;
    public event EventHandler? OpenCmdRequested;
    public event EventHandler? EnvironmentVariablesRequested;
    public event EventHandler? EditCredentialRequested;

    public AccountContextMenuOrchestrator(
        AccountContextMenuHandler accountHandler,
        ContainerContextMenuHandler containerHandler,
        AccountFirewallMenuHandler firewallHandler,
        AccountProcessMenuHandler processMenuHandler)
    {
        _accountHandler = accountHandler;
        _containerHandler = containerHandler;
        _firewallHandler = firewallHandler;
        _processMenuHandler = processMenuHandler;

        _accountHandler.AppNavigationRequested += sid => AppNavigationRequested?.Invoke(sid);
        _accountHandler.NewAppRequested += sid => NewAppRequested?.Invoke(sid);
        _accountHandler.InstallRequested += pkg => InstallRequested?.Invoke(pkg);
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

    private void WireItemClickEvents(ToolStripMenuItem hdrCreateContainer)
    {
        Items.AclManager.Click += (_, _) => OpenAclManager();
        Items.FolderBrowser.Click += (s, e) => OpenFolderBrowserRequested?.Invoke(s, e);
        Items.Cmd.Click += (s, e) => OpenCmdRequested?.Invoke(s, e);
        Items.EnvironmentVariables.Click += (s, e) => EnvironmentVariablesRequested?.Invoke(s, e);
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
        Items.ContainerFolderBrowser.Click += (_, _) =>
        {
            if (GetSelectedContainerRow() is { } cr)
                _containerHandler.OpenContainerFolderBrowser(cr);
        };

        foreach (var (package, item) in Items.InstallItems)
        {
            var capturedPackage = package;
            item.Click += (_, _) => _accountHandler.InstallPackage(capturedPackage);
        }
    }

    private AccountRow? GetSelectedAccountRow()
        => _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag as AccountRow : null;

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
        HideAllAccountItems(i);
        i.FirewallAllowlist.Visible = false;
        i.AppsSeparator.Visible = false;
        i.NewApp.Visible = false;
        i.ContainerSeparator.Visible = false;
        i.CreateContainer.Visible = false;
        AccountContextMenuHandler.SetContainerItemsVisible(i, false);
        i.ContainerFolderBrowser.Visible = false;

        _processMenuHandler.ShowProcessMenu(processRow);
    }

    private static void HideAllAccountItems(AccountContextMenuItems i)
    {
        i.AddCredential.Visible = false;
        i.AddCredentialSeparator.Visible = false;
        i.EditSubmenu.Visible = false;
        i.EditSeparator.Visible = false;
        i.PinFolderBrowserToTray.Visible = false;
        i.PinDiscoveryToTray.Visible = false;
        i.PinTerminalToTray.Visible = false;
        i.CopySid.Visible = false;
        i.CopyProfilePath.Visible = false;
        i.OpenProfileFolder.Visible = false;
        i.CopyPassword.Visible = false;
        i.TypePassword.Visible = false;
        i.Sep4.Visible = false;
        i.Sep5.Visible = false;
        i.ManageSeparator.Visible = false;
        i.ManageSubmenu.Visible = false;
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