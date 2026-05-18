using RunFence.Core.Models;
using RunFence.Firewall.UI;

namespace RunFence.Firewall.UI.Forms;

/// <summary>
/// Headless-testable contract for the firewall allowlist dialog lifecycle and applied state.
/// </summary>
public interface IFirewallAllowlistDialog : IDisposable
{
    event EventHandler<FirewallApplyEventArgs>? Applied;

    List<FirewallAllowlistEntry> Result { get; }
    bool AllowInternet { get; }
    bool AllowLan { get; }
    bool AllowLocalhost { get; }
    IReadOnlyList<string> AllowedLocalhostPorts { get; }
    bool FilterEphemeralLoopback { get; }

    void AutoOpenBlockedConnectionsOnShow();
    DialogResult ShowDialog(IWin32Window? owner);
}
