using RunFence.Core.Models;
using RunFence.Firewall.UI;

namespace RunFence.Firewall.UI.Forms;

/// <summary>
/// Factory for creating firewall-related dialogs. Holds the DI-resolved dependencies
/// (network info, validators, resolvers) and accepts only per-invocation data as parameters.
/// Registered in DI as <c>SingleInstance</c>; dialogs are <c>InstancePerDependency</c>-equivalent
/// (each Create call returns a new instance).
/// </summary>
public class FirewallDialogFactory(
    IFirewallNetworkInfo? firewallNetworkInfo,
    FirewallAllowlistValidator validator,
    FirewallPortValidator portValidator,
    FirewallDomainResolver domainResolver,
    BlockedConnectionAggregator aggregator,
    FirewallAllowlistImportExportService importExportService,
    IBlockedConnectionReader? blockedConnectionReader,
    IDnsResolver? dnsResolver)
{
    /// <summary>Whether firewall network info is available (i.e. firewall is configured).</summary>
    public bool IsAvailable => firewallNetworkInfo != null;

    /// <summary>
    /// Creates a new <see cref="FirewallAllowlistDialog"/> for the given account.
    /// Returns <c>null</c> when firewall network info is unavailable.
    /// </summary>
    public FirewallAllowlistDialog? CreateAllowlistDialog(
        List<FirewallAllowlistEntry> current,
        string? displayName,
        bool allowInternet,
        bool allowLan,
        bool allowLocalhost,
        IReadOnlyList<string>? allowedLocalhostPorts,
        bool filterEphemeralLoopback = true)
    {
        if (firewallNetworkInfo == null)
            return null;

        return new FirewallAllowlistDialog(
            current: current,
            firewallNetworkInfo: firewallNetworkInfo,
            validator: validator,
            portValidator: portValidator,
            domainResolver: domainResolver,
            aggregator: aggregator,
            importExportService: importExportService,
            displayName: displayName,
            allowInternet: allowInternet,
            allowLan: allowLan,
            allowLocalhost: allowLocalhost,
            allowedLocalhostPorts: allowedLocalhostPorts,
            blockedConnectionReader: blockedConnectionReader,
            dnsResolver: dnsResolver,
            filterEphemeralLoopback: filterEphemeralLoopback);
    }

    /// <summary>
    /// Creates a new <see cref="BlockedConnectionsDialog"/>.
    /// Returns <c>null</c> when blocked connection reader or DNS resolver is unavailable.
    /// </summary>
    public BlockedConnectionsDialog? CreateBlockedConnectionsDialog(
        IReadOnlyList<FirewallAllowlistEntry> existingAllowlist,
        bool enableAuditLogging)
    {
        if (blockedConnectionReader == null || dnsResolver == null)
            return null;

        return new BlockedConnectionsDialog(
            reader: blockedConnectionReader,
            dnsResolver: dnsResolver,
            aggregator: aggregator,
            existingAllowlist: existingAllowlist,
            enableAuditLogging: enableAuditLogging);
    }

    /// <summary>
    /// Enables audit logging if the blocked connection reader is available.
    /// Swallows exceptions — audit logging is best-effort.
    /// </summary>
    public void TrySetAuditPolicyEnabled(bool enabled)
    {
        if (blockedConnectionReader == null)
            return;
        try { blockedConnectionReader.SetAuditPolicyEnabled(enabled); } catch { }
    }
}
