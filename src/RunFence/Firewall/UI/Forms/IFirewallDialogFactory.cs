using RunFence.Core.Models;

namespace RunFence.Firewall.UI.Forms;

/// <summary>
/// Factory for creating firewall-related dialogs behind interfaces suitable for headless tests.
/// </summary>
public interface IFirewallDialogFactory
{
    bool IsAvailable { get; }

    IFirewallAllowlistDialog? CreateAllowlistDialog(
        List<FirewallAllowlistEntry> current,
        string? displayName,
        bool allowInternet,
        bool allowLan,
        bool allowLocalhost,
        IReadOnlyList<string>? allowedLocalhostPorts,
        bool filterEphemeralLoopback = true);
}
