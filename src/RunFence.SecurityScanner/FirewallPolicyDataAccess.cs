using Microsoft.Win32;

namespace RunFence.SecurityScanner;

public class FirewallPolicyDataAccess(IFirewallServiceNativeReader nativeReader) : IFirewallPolicyDataAccess
{
    public List<(string ProfileName, bool Enabled)>? GetFirewallProfileStates()
    {
        try
        {
            const string firewallPolicyKey = @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy";
            var profiles = new (string SubKey, string DisplayName)[]
            {
                ("DomainProfile", "Domain"),
                ("StandardProfile", "Private"),
                ("PublicProfile", "Public"),
            };

            var result = new List<(string, bool)>();
            using var policyKey = Registry.LocalMachine.OpenSubKey(firewallPolicyKey);
            if (policyKey == null)
                return null;

            foreach (var (subKey, displayName) in profiles)
            {
                using var profileKey = policyKey.OpenSubKey(subKey);
                if (profileKey?.GetValue("EnableFirewall") is int enabled)
                    result.Add((displayName, enabled != 0));
            }

            return result.Count > 0 ? result : null;
        }
        catch
        {
            return null;
        }
    }

    public (bool IsDisabled, bool IsStopped)? GetWindowsFirewallServiceState() =>
        nativeReader.GetWindowsFirewallServiceState();
}
