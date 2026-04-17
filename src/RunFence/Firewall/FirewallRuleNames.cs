namespace RunFence.Firewall;

/// <summary>
/// Pure string-based naming helpers for RunFence firewall rules.
/// All methods are non-IO, side-effect free, and never needing mocking.
/// </summary>
public static class FirewallRuleNames
{
    public static string InternetIPv4RuleName(string username) => $"RunFence Block Internet IPv4 ({username})";
    public static string InternetIPv6RuleName(string username) => $"RunFence Block Internet IPv6 ({username})";
    public static string LocalhostIPv4RuleName(string username) => $"RunFence Block Localhost IPv4 ({username})";
    public static string LocalhostIPv6RuleName(string username) => $"RunFence Block Localhost IPv6 ({username})";
    public static string LocalAddressIPv4RuleName(string username) => $"RunFence Block Local Addresses IPv4 ({username})";
    public static string LocalAddressIPv6RuleName(string username) => $"RunFence Block Local Addresses IPv6 ({username})";
    public static string LanIPv4RuleName(string username) => $"RunFence Block LAN IPv4 ({username})";
    public static string LanIPv6RuleName(string username) => $"RunFence Block LAN IPv6 ({username})";

    public static string GetRuleNamePrefix(string ruleName)
    {
        var parenIndex = ruleName.IndexOf('(');
        return parenIndex >= 0 ? ruleName[..(parenIndex + 1)] : ruleName;
    }
}
