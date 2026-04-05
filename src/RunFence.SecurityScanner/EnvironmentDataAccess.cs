using System.DirectoryServices.AccountManagement;
using System.Security.Principal;
using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.SecurityScanner;

public class EnvironmentDataAccess(NativePolicyDataAccess nativePolicy, Action<string> logError)
{
    public string? GetPublicStartupPath()
    {
        try
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
        }
        catch
        {
            return null;
        }
    }

    public string? GetCurrentUserStartupPath()
    {
        try
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        }
        catch
        {
            return null;
        }
    }

    public string? GetCurrentUserSid()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User?.Value;
        }
        catch
        {
            return null;
        }
    }

    public string? GetInteractiveUserSid() =>
        NativeTokenHelper.TryGetInteractiveUserSid()?.Value;

    public string? GetInteractiveUserProfilePath(string sid)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"{Constants.ProfileListRegistryKey}\{sid}");
            var raw = key?.GetValue("ProfileImagePath") as string;
            return string.IsNullOrEmpty(raw) ? null : SecurityScanner.ExpandEnvVars(raw);
        }
        catch
        {
            return null;
        }
    }

    public HashSet<string> GetAdminMemberSids()
    {
        var sids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var adminGroup = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, "S-1-5-32-544");
            if (adminGroup != null)
            {
                // First pass: recursive individual users
                foreach (var member in adminGroup.GetMembers(true))
                {
                    try
                    {
                        if (member.Sid != null)
                            sids.Add(member.Sid.Value);
                    }
                    catch
                    {
                        /* skip unresolvable members */
                    }
                    finally
                    {
                        member.Dispose();
                    }
                }

                // Second pass: direct members (captures group SIDs like Domain Admins)
                foreach (var member in adminGroup.GetMembers(false))
                {
                    try
                    {
                        if (member.Sid != null)
                            sids.Add(member.Sid.Value);
                    }
                    catch
                    {
                        /* skip unresolvable members */
                    }
                    finally
                    {
                        member.Dispose();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logError($"Failed to enumerate admin group members: {ex.Message}");
        }

        // Always include the group SID itself
        sids.Add("S-1-5-32-544");
        return sids;
    }

    public List<(string Sid, string? ProfilePath)> GetAllLocalUserProfiles()
    {
        var profiles = new List<(string, string?)>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(Constants.ProfileListRegistryKey);
            if (key == null)
                return profiles;

            foreach (var subKeyName in key.GetSubKeyNames())
            {
                try
                {
                    if (!subKeyName.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase))
                        continue;

                    using var subKey = key.OpenSubKey(subKeyName);
                    var raw = subKey?.GetValue("ProfileImagePath") as string;
                    var profilePath = string.IsNullOrEmpty(raw) ? null : SecurityScanner.ExpandEnvVars(raw);
                    profiles.Add((subKeyName, profilePath));
                }
                catch
                {
                    /* skip unreadable profile entries */
                }
            }
        }
        catch (Exception ex)
        {
            logError($"Failed to enumerate user profiles: {ex.Message}");
        }

        return profiles;
    }

    public HashSet<string>? TryGetGroupMemberSids(string groupSid)
    {
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var group = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, groupSid);
            if (group == null)
                return null;

            var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in group.GetMembers(false))
            {
                try
                {
                    if (member.Sid != null)
                        members.Add(member.Sid.Value);
                }
                catch
                {
                    /* skip unresolvable members */
                }
                finally
                {
                    member.Dispose();
                }
            }

            return members;
        }
        catch
        {
            return null;
        }
    }

    public string ResolveDisplayName(string sidString)
    {
        try
        {
            var sid = new SecurityIdentifier(sidString);
            return sid.Translate(typeof(NTAccount)).Value;
        }
        catch
        {
            return sidString;
        }
    }

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

    public int? GetAccountLockoutThreshold() => nativePolicy.GetAccountLockoutThreshold();

    public (bool IsDisabled, bool IsStopped)? GetWindowsFirewallServiceState() =>
        nativePolicy.GetWindowsFirewallServiceState();

    public bool? GetAdminAccountLockoutEnabled() => nativePolicy.GetAdminAccountLockoutEnabled();

    public bool? GetBlankPasswordRestrictionEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Lsa");
            if (key?.GetValue("LimitBlankPasswordUse") is int value)
                return value != 0;
            return null;
        }
        catch
        {
            return null;
        }
    }
}