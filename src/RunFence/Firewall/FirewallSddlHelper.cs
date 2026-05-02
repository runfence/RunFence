using System.Text.RegularExpressions;

namespace RunFence.Firewall;

public static partial class FirewallSddlHelper
{
    [GeneratedRegex(@"D:\(A;;CC;;;([^)]+)\)")]
    private static partial Regex SddlSidPattern();

    public static string BuildSddl(string sid) => $"D:(A;;CC;;;{sid})";

    public static string? ExtractSid(string sddl)
    {
        var match = SddlSidPattern().Match(sddl);
        return match.Success ? match.Groups[1].Value : null;
    }
}
