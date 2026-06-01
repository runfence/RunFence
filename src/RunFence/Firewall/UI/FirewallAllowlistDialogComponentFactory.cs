using RunFence.Firewall.UI.Forms;

namespace RunFence.Firewall.UI;

public sealed class FirewallAllowlistDialogComponentFactory(
    IFirewallNetworkInfo firewallNetworkInfo,
    FirewallAllowlistValidator validator,
    FirewallPortValidator portValidator,
    FirewallDomainResolver domainResolver,
    BlockedConnectionsFlowHelper blockedConnectionsFlow,
    FirewallAllowlistImportExportService importExportService,
    FirewallDialogApplyPresenter applyPresenter)
{
    public (FirewallAllowlistTabHandler AllowlistHandler, FirewallPortsTabHandler PortsHandler) CreateTabHandlers(
        FirewallAllowlistInitialState state)
    {
        return (
            new FirewallAllowlistTabHandler(validator, domainResolver, state.Current),
            new FirewallPortsTabHandler(portValidator, state.AllowedLocalhostPorts));
    }

    public FirewallAllowlistImportExportHelper CreateImportExportHelper(
        FirewallAllowlistTabHandler allowlistHandler,
        FirewallPortsTabHandler portsHandler,
        IFirewallAllowlistDialogView view)
    {
        return new FirewallAllowlistImportExportHelper(
            importExportService,
            allowlistHandler.GetEntries,
            portsHandler.GetPortEntries,
            view);
    }

    public FirewallAllowlistEntriesController CreateEntriesController(
        IFirewallAllowlistDialogView view,
        FirewallAllowlistTabHandler allowlistHandler,
        FirewallAllowlistImportExportHelper importExportHelper)
    {
        var allowlistGridHelper = new FirewallAllowlistGridHelper(
            view.AllowlistGrid,
            allowlistHandler,
            importExportHelper.TryExportToFile,
            importExportHelper.TryExportCombinedToFile,
            view.UpdateApplyButton,
            view.RefreshToolbarState,
            view.ShowInformation,
            view.ShowWarning,
            view.ShowError);
        return new FirewallAllowlistEntriesController(view, allowlistHandler, allowlistGridHelper);
    }

    public FirewallAllowlistPortsController CreatePortsController(
        IFirewallAllowlistDialogView view,
        FirewallPortsTabHandler portsHandler,
        FirewallAllowlistImportExportHelper importExportHelper)
    {
        var portsGridHelper = new FirewallPortsGridHelper(
            view.PortsGrid,
            portsHandler,
            importExportHelper.TryExportToFile,
            importExportHelper.TryExportCombinedToFile,
            view.UpdateApplyButton,
            view.ShowInformation,
            view.ShowWarning);
        return new FirewallAllowlistPortsController(view, portsHandler, portsGridHelper);
    }

    public FirewallBlockedConnectionsDialogController CreateBlockedConnectionsController(
        IFirewallAllowlistDialogView view,
        FirewallAllowlistEntriesController allowlistEntriesController)
    {
        return new FirewallBlockedConnectionsDialogController(
            view,
            blockedConnectionsFlow,
            allowlistEntriesController);
    }

    public FirewallAllowlistDialogCoordinator CreateDialogCoordinator(
        FirewallAllowlistInitialState state,
        IFirewallAllowlistDialogView view,
        FirewallAllowlistTabHandler allowlistHandler,
        FirewallPortsTabHandler portsHandler)
    {
        return new FirewallAllowlistDialogCoordinator(
            view,
            firewallNetworkInfo,
            allowlistHandler,
            portsHandler,
            applyPresenter,
            state.AllowInternet,
            state.AllowLan,
            state.AllowLocalhost,
            state.FilterEphemeralLoopback);
    }

    public FirewallAllowlistImportExportCoordinator CreateImportExportCoordinator(
        FirewallAllowlistImportExportHelper importExportHelper,
        FirewallAllowlistEntriesController allowlistEntriesController,
        FirewallAllowlistPortsController portsController,
        IFirewallAllowlistDialogView view)
    {
        return new FirewallAllowlistImportExportCoordinator(
            importExportHelper,
            allowlistEntriesController,
            portsController,
            view);
    }
}
