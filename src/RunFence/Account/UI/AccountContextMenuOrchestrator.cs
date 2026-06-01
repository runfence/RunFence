using System.ComponentModel;
using RunFence.Acl.UI;
using RunFence.Core.Models;
using RunFence.Launch;
using RunFence.UI;

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
    private readonly FullModeAccountLaunchIdentityFactory _fullModeIdentityFactory;

    private DataGridView _grid = null!;
    private ContextMenuStrip _contextMenu = null!;
    private IAccountsPanelOperationContext _panelContext = null!;

    public event Action<string>? AppNavigationRequested;
    public event Action<string>? NewAppRequested;
    public event Action? DataChangedAndRefresh;
    public event EventHandler? EditCredentialRequested;
    public event Action? ShowSystemInRunAsToggleRequested;

    public AccountContextMenuOrchestrator(
        AccountContextMenuHandler accountHandler,
        ContainerContextMenuHandler containerHandler,
        AccountFirewallMenuHandler firewallHandler,
        AccountProcessMenuHandler processMenuHandler,
        ToolLauncher launchService,
        AccountTrayToggleService trayToggleService,
        ISidNameCacheService sidNameCache,
        FullModeAccountLaunchIdentityFactory fullModeIdentityFactory)
    {
        _accountHandler = accountHandler;
        _containerHandler = containerHandler;
        _firewallHandler = firewallHandler;
        _processMenuHandler = processMenuHandler;
        _launchService = launchService;
        _trayToggleService = trayToggleService;
        _sidNameCache = sidNameCache;
        _fullModeIdentityFactory = fullModeIdentityFactory;

        _accountHandler.AppNavigationRequested += sid => AppNavigationRequested?.Invoke(sid);
        _accountHandler.NewAppRequested += sid => NewAppRequested?.Invoke(sid);
        _containerHandler.DataChangedAndRefresh += () => DataChangedAndRefresh?.Invoke();
        _firewallHandler.SaveAndRefreshRequested += () => DataChangedAndRefresh?.Invoke();
    }

    public void Initialize(DataGridView grid, ContextMenuStrip contextMenu,
        IAccountsPanelOperationContext panelContext,
        ToolStripMenuItem hdrCreateContainer, AccountProcessDisplayManager? processDisplayManager = null)
    {
        _grid = grid;
        _contextMenu = contextMenu;
        _panelContext = panelContext;
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
        Items.Cmd.Click += async (_, _) => await OpenCmdWithTerminalLaunchRefreshAsync();
        Items.EnvironmentVariables.Click += (_, _) => OpenEnvironmentVariables();
        Items.KillAllProcesses.Click += async (_, _) =>
        {
            if (GetSelectedAccountRow() is { } ar)
                await RunKillAllProcessesAsync(ar);
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
        Items.ShowInRunAs.Click += (_, _) => ShowSystemInRunAsToggleRequested?.Invoke();

        Items.CreateContainer.Click += (_, _) => _containerHandler.CreateContainer();
        hdrCreateContainer.Click += (_, _) => _containerHandler.CreateContainer();
        Items.EditContainer.Click += async (_, _) =>
        {
            if (GetSelectedContainerRow() is { } cr)
                await _containerHandler.EditContainer(cr);
        };
        Items.DeleteContainer.Click += async (_, _) =>
        {
            if (GetSelectedContainerRow() is { } cr)
                await _containerHandler.DeleteContainer(cr);
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

        foreach (var (package, item) in Items.InstallItems)
        {
            var capturedPackage = package;
            item.Click += async (_, _) => await HandleInstallRequestAsync(capturedPackage);
        }
    }

    public async Task OpenCmdAsync()
    {
        if (!TryCreateSelectedCmdLaunchRequest(null, out var request))
            return;

        await _launchService.OpenCmdAsync(request.Identity);
    }

    public async Task OpenCmdWithTerminalLaunchRefreshAsync()
        => await OpenCmdWithTerminalLaunchRefreshAsync(null);

    public async Task OpenCmdWithTerminalLaunchRefreshAsync(PrivilegeLevel? explicitPrivilegeLevel)
    {
        if (!TryCreateSelectedCmdLaunchRequest(explicitPrivilegeLevel, out var request))
            return;

        await ExecuteBusyAccountActionAsync(_panelContext, Items, async () =>
        {
            await _launchService.OpenCmdAsync(request.Identity, request.RequestTerminalRefresh);
        }, refreshAfter: false);
    }

    public void OpenFolderBrowser()
        => OpenFolderBrowser(null);

    public void OpenFolderBrowser(PrivilegeLevel? explicitPrivilegeLevel)
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
        _launchService.OpenFolderBrowser(CreateAccountLaunchIdentity(accountRow, explicitPrivilegeLevel), permissionPrompt);
    }

    public void OpenEnvironmentVariables()
    {
        if (_grid.SelectedRows.Count == 0 || _grid.SelectedRows[0].Tag is not AccountRow accountRow)
            return;
        var shift = (Control.ModifierKeys & Keys.Shift) != 0;
        _launchService.OpenEnvironmentVariables(new AccountLaunchIdentity(accountRow.Sid)
            { PrivilegeLevel = shift ? PrivilegeLevel.HighestAllowed : null });
    }

    public async Task HandleInstallRequestAsync(InstallablePackage package)
    {
        if (GetSelectedAccountRow() is not { } ar)
            return;

        var identity = new AccountLaunchIdentity(ar.Sid);
        await ExecuteBusyAccountActionAsync(_panelContext, Items, () => _launchService.InstallPackageAsync(package, identity));
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

    public void ToggleReceiveInjectedInput()
    {
        if (GetSelectedAccountRow() is not { } ar || string.IsNullOrEmpty(ar.Sid))
            return;
        _trayToggleService.ToggleReceiveInjectedInput(ar.Sid, () => DataChangedAndRefresh?.Invoke());
    }

    private AccountRow? GetSelectedAccountRow()
        => AccountGridHelper.GetSelectedAccountRow(_grid);

    private ContainerRow? GetSelectedContainerRow()
        => _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag as ContainerRow : null;

    private bool TryCreateSelectedCmdLaunchRequest(PrivilegeLevel? explicitPrivilegeLevel, out CmdLaunchRequest request)
    {
        request = default;
        if (_grid.SelectedRows.Count == 0)
            return false;

        if (_grid.SelectedRows[0].Tag is ContainerRow containerRow)
        {
            if (explicitPrivilegeLevel != null)
                return false;

            request = new CmdLaunchRequest(new AppContainerLaunchIdentity(containerRow.Container), RequestTerminalRefresh: false);
            return true;
        }

        if (_grid.SelectedRows[0].Tag is not AccountRow accountRow)
            return false;

        var accountIdentity = CreateAccountLaunchIdentity(accountRow, explicitPrivilegeLevel);
        request = new CmdLaunchRequest(
            accountIdentity,
            RequestTerminalRefresh: true);
        return true;
    }

    private AccountLaunchIdentity CreateAccountLaunchIdentity(AccountRow accountRow, PrivilegeLevel? explicitPrivilegeLevel)
    {
        if (explicitPrivilegeLevel != null)
            return new AccountLaunchIdentity(accountRow.Sid) { PrivilegeLevel = explicitPrivilegeLevel };

        var shift = (Control.ModifierKeys & Keys.Shift) != 0;
        return _fullModeIdentityFactory.Create(accountRow.Sid, fullMode: shift);
    }

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

    private async Task RunKillAllProcessesAsync(AccountRow accountRow)
    {
        await ExecuteBusyAccountActionAsync(_panelContext, Items, () => _accountHandler.KillAllProcessesAsync(accountRow));
    }

    private static async Task ExecuteBusyAccountActionAsync(
        IAccountsPanelOperationContext panelContext,
        AccountContextMenuItems items,
        Func<Task> action,
        bool refreshAfter = true)
    {
        SetBusyAccountActions(items, enabled: false);
        panelContext.SetControlsEnabled(false);
        panelContext.OperationGuard.Begin(panelContext.OwnerControl);
        try
        {
            await action();
        }
        finally
        {
            panelContext.OperationGuard.End(panelContext.OwnerControl);
            panelContext.SetControlsEnabled(true);
            SetBusyAccountActions(items, enabled: true);
            panelContext.UpdateButtonState();
            if (refreshAfter)
            {
                panelContext.RefreshGrid();
                var generation = panelContext.BeginProcessRefreshGeneration();
                panelContext.TriggerProcessRefresh(generation, 1000);
            }
        }
    }

    private static void SetBusyAccountActions(AccountContextMenuItems items, bool enabled)
    {
        items.ManageSubmenu.Enabled = enabled;
        items.EditSubmenu.Enabled = enabled;
        items.AddCredential.Enabled = enabled;
        items.CopyPassword.Enabled = enabled;
        items.TypePassword.Enabled = enabled;
        items.NewApp.Enabled = enabled;
        items.KillAllProcesses.Enabled = enabled;
        items.ManageAssociations.Enabled = enabled;
        items.ReceiveInjectedInput.Enabled = enabled;
    }

    private readonly record struct CmdLaunchRequest(LaunchIdentity Identity, bool RequestTerminalRefresh);
}
