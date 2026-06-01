using RunFence.Core.Models;
using RunFence.Infrastructure;

namespace RunFence.Firewall.UI.Forms;

/// <summary>
/// Factory for creating firewall-related dialogs. Holds the DI-resolved dependencies
/// (network info, validators, resolvers) and accepts only per-invocation data as parameters.
/// Registered in DI as <c>SingleInstance</c>; dialogs are <c>InstancePerDependency</c>-equivalent
/// (each Create call returns a new instance).
/// </summary>
public class FirewallDialogFactory(
    IFirewallNetworkInfo? firewallNetworkInfo,
    FirewallAllowlistDialogComponentFactory componentFactory) : IFirewallDialogFactory
{
    /// <summary>Whether firewall network info is available (i.e. firewall is configured).</summary>
    public bool IsAvailable => firewallNetworkInfo != null;

    /// <summary>
    /// Creates a new <see cref="FirewallAllowlistDialog"/> for the given account.
    /// Returns <c>null</c> when firewall network info is unavailable.
    /// </summary>
    public IFirewallAllowlistDialog? CreateAllowlistDialog(
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
            new FirewallAllowlistInitialState(
                current,
                displayName,
                allowInternet,
                allowLan,
                allowLocalhost,
                allowedLocalhostPorts,
                filterEphemeralLoopback),
            componentFactory);
    }
}
