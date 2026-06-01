using RunFence.Core.Models;

namespace RunFence.Firewall.UI;

public interface IBlockedConnectionsDialogFlow
{
    List<FirewallAllowlistEntry>? ShowDialog(
        IReadOnlyList<FirewallAllowlistEntry> existingEntries,
        IWin32Window owner,
        bool enableAuditLogging = false);
}
