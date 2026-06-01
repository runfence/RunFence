using RunFence.Core.Models;

namespace RunFence.Firewall.UI;

public sealed record FirewallAllowlistInitialState(
    List<FirewallAllowlistEntry> Current,
    string? DisplayName = null,
    bool AllowInternet = true,
    bool AllowLan = true,
    bool AllowLocalhost = true,
    IReadOnlyList<string>? AllowedLocalhostPorts = null,
    bool FilterEphemeralLoopback = true);
