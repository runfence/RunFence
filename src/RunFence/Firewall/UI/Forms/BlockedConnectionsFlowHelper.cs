using RunFence.Core.Models;

namespace RunFence.Firewall.UI.Forms;

/// <summary>
/// Encapsulates the blocked-connections workflow: reading from the event log,
/// aggregating by address, and showing the selection dialog.
/// Allows <see cref="FirewallAllowlistDialog"/> to avoid direct dependencies on
/// <see cref="IBlockedConnectionReader"/> and <see cref="BlockedConnectionAggregator"/>.
/// </summary>
public class BlockedConnectionsFlowHelper(
    BlockedConnectionAggregator aggregator,
    IBlockedConnectionReader blockedConnectionReader,
    IDnsResolver dnsResolver)
{
    /// <summary>
    /// Shows the blocked-connections dialog and returns the user-selected allowlist entries,
    /// or <c>null</c> if the user cancelled.
    /// </summary>
    /// <param name="existingEntries">The current allowlist entries to pass to the dialog.</param>
    /// <param name="owner">The owner window for the dialog.</param>
    /// <param name="enableAuditLogging">
    /// When <c>true</c>, activates audit logging immediately — intended for the post-wizard flow.
    /// </param>
    public List<FirewallAllowlistEntry>? ShowDialog(
        IReadOnlyList<FirewallAllowlistEntry> existingEntries,
        IWin32Window owner,
        bool enableAuditLogging = false)
    {
        using var dlg = new BlockedConnectionsDialog(
            blockedConnectionReader, dnsResolver,
            aggregator, existingEntries,
            enableAuditLogging: enableAuditLogging);

        return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.SelectedEntries : null;
    }
}
