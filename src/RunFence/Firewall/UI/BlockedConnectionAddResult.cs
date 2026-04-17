using RunFence.Core.Models;

namespace RunFence.Firewall.UI;

/// <summary>
/// Result of adding entries from blocked connections to the allowlist.
/// </summary>
/// <param name="Added">The entries that were successfully added.</param>
/// <param name="TruncatedCount">The number of entries that could not be added due to the license limit.</param>
public record BlockedConnectionAddResult(
    IReadOnlyList<FirewallAllowlistEntry> Added,
    int TruncatedCount);
