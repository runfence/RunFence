using System.Text.RegularExpressions;

namespace RunFence.Firewall;

public static class FirewallSddlHelper
{
    private static readonly Regex SddlSidPattern = new(@"D:\(A;;CC;;;([^)]+)\)", RegexOptions.Compiled);

    public static string BuildSddl(string sid) => $"D:(A;;CC;;;{sid})";

    public static string? ExtractSid(string sddl)
    {
        var match = SddlSidPattern.Match(sddl);
        return match.Success ? match.Groups[1].Value : null;
    }
}
