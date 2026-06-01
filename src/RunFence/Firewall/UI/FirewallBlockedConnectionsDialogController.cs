namespace RunFence.Firewall.UI;

public sealed class FirewallBlockedConnectionsDialogController(
    IFirewallAllowlistDialogView view,
    IBlockedConnectionsDialogFlow blockedConnectionsFlow,
    FirewallAllowlistEntriesController allowlistEntriesController)
{
    public void OpenDialog(bool enableAuditLogging = false)
    {
        var selectedEntries = blockedConnectionsFlow.ShowDialog(
            allowlistEntriesController.GetEntries(),
            view,
            enableAuditLogging);
        if (selectedEntries == null)
            return;

        var addResult = allowlistEntriesController.AddEntriesFromBlockedConnections(selectedEntries);
        if (addResult.TruncatedCount <= 0)
            return;

        var limitMessage = allowlistEntriesController.GetLicenseLimitMessage();
        if (limitMessage != null)
            view.ShowInformation("License Limit", limitMessage);
    }
}
