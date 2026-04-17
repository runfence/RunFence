using System.DirectoryServices.AccountManagement;
using System.Security.Principal;
using Microsoft.Win32;
using RunFence.Core;

namespace RunFence.SecurityScanner;

/// <summary>
/// Provides environmental context for the security scanner: user SIDs, profiles, admin members,
/// group membership, and firewall/policy state.
/// </summary>
/// <remarks>
/// Depends on concrete <see cref="NativePolicyDataAccess"/> directly — acceptable for a standalone
/// scanner tool without DI. If testability is needed in the future, extract an interface for the
/// 3 methods used: <c>GetLocalDomainSid</c>, <c>ResolveLocalGroupNames</c>, <c>GetLocalGroupMemberSids</c>.
/// </remarks>
public class EnvironmentDataAccess(NativePolicyDataAccess nativePolicy, Action<string> logError, NTTranslateApi ntTranslate) : IEnvironmentDataAccess
{
    // Pre-populated by GetAdminMemberSids() to avoid re-opening a PrincipalContext for the same group.
    private readonly Dictionary<string, HashSet<string>> _directMembersCache = new(StringComparer.OrdinalIgnoreCase);

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
        var directMembers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var context = new PrincipalContext(ContextType.Machine);
            using var adminGroup = GroupPrincipal.FindByIdentity(context, IdentityType.Sid, "S-1-5-32-544");
            if (adminGroup != null)
            {
                // First pass: direct members — captures group SIDs like Domain Admins.
                // Also pre-populates _directMembersCache so TryGetGroupMemberSids("S-1-5-32-544")
                // returns from cache without opening a second PrincipalContext.
                foreach (var member in adminGroup.GetMembers(false))
                {
                    try
                    {
                        if (member.Sid != null)
                        {
                            sids.Add(member.Sid.Value);
                            directMembers.Add(member.Sid.Value);
                        }
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

                // Second pass: recursive individual users
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
            }
        }
        catch (Exception ex)
        {
            logError($"Failed to enumerate admin group members: {ex.Message}");
        }

        _directMembersCache["S-1-5-32-544"] = directMembers;
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
        if (_directMembersCache.TryGetValue(groupSid, out var cachedDirect))
            return cachedDirect;

        var results = BulkLookupGroupMemberSids([groupSid]);
        return results.GetValueOrDefault(groupSid);
    }

    public Dictionary<string, HashSet<string>?> BulkLookupGroupMemberSids(IReadOnlyList<string> sids)
    {
        var result = new Dictionary<string, HashSet<string>?>(StringComparer.OrdinalIgnoreCase);
        var localDomainSid = nativePolicy.GetLocalDomainSid();
        var toResolve = new List<string>(sids.Count);

        foreach (var sid in sids)
        {
            if (_directMembersCache.TryGetValue(sid, out var cached))
            {
                result[sid] = cached;
                continue;
            }

            // Domain SIDs (non-local S-1-5-21-*) cannot be resolved via local SAM.
            // Return null so they are reported as findings rather than triggering a DC lookup.
            if (localDomainSid != null
                && sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase)
                && !sid.StartsWith(localDomainSid + "-", StringComparison.OrdinalIgnoreCase))
            {
                result[sid] = null;
                continue;
            }

            toResolve.Add(sid);
        }

        if (toResolve.Count == 0)
            return result;

        var localGroupNames = nativePolicy.ResolveLocalGroupNames(toResolve);
        foreach (var sid in toResolve)
        {
            if (localGroupNames.TryGetValue(sid, out var groupName))
            {
                var members = nativePolicy.GetLocalGroupMemberSids(groupName);
                result[sid] = members;
                if (members != null)
                    _directMembersCache[sid] = members;
            }
            else
            {
                result[sid] = null; // not a local group (user SID, well-known, unresolvable)
            }
        }

        return result;
    }

    public string ResolveDisplayName(string sidString)
    {
        try
        {
            return ntTranslate.TranslateName(sidString).Value;
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