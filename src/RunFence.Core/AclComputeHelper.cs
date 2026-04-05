using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.Core;

public static class AclComputeHelper
{
    public static readonly SecurityIdentifier AdministratorsSid =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);

    public static readonly SecurityIdentifier LocalSystemSid =
        new(WellKnownSidType.LocalSystemSid, null);

    public static readonly SecurityIdentifier CreatorOwnerSid =
        new(WellKnownSidType.CreatorOwnerSid, null);

    // NT SERVICE\TrustedInstaller
    public static readonly SecurityIdentifier TrustedInstallerSid =
        new("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");

    public static Dictionary<string, FileSystemRights> ComputeEffectiveFileRights(
        FileSystemSecurity security, string? ownerSid, bool skipTrustedSids = true)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ownerSid != null)
            excluded.Add(ownerSid);
        return ComputeEffectiveFileRights(security, excluded, skipTrustedSids);
    }

    public static Dictionary<string, FileSystemRights> ComputeEffectiveFileRights(
        FileSystemSecurity security, HashSet<string> excludedSids, bool skipTrustedSids = true)
    {
        var allowed = new Dictionary<string, FileSystemRights>(StringComparer.OrdinalIgnoreCase);
        var denied = new Dictionary<string, FileSystemRights>(StringComparer.OrdinalIgnoreCase);

        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            var sid = (SecurityIdentifier)rule.IdentityReference;
            if (skipTrustedSids && IsTrustedSid(sid, excludedSids))
                continue;

            AccumulateRights(allowed, denied, sid.Value, rule.AccessControlType, rule.FileSystemRights);
        }

        return SubtractDenied(allowed, denied);
    }

    public static Dictionary<string, RegistryRights> ComputeEffectiveRegistryRights(
        RegistrySecurity security, string? ownerSid, bool skipTrustedSids = true)
    {
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (ownerSid != null)
            excluded.Add(ownerSid);
        return ComputeEffectiveRegistryRights(security, excluded, skipTrustedSids);
    }

    public static Dictionary<string, RegistryRights> ComputeEffectiveRegistryRights(
        RegistrySecurity security, HashSet<string> excludedSids, bool skipTrustedSids = true)
    {
        var allowed = new Dictionary<string, RegistryRights>(StringComparer.OrdinalIgnoreCase);
        var denied = new Dictionary<string, RegistryRights>(StringComparer.OrdinalIgnoreCase);

        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
        foreach (RegistryAccessRule rule in rules)
        {
            var sid = (SecurityIdentifier)rule.IdentityReference;
            if (skipTrustedSids && IsTrustedSid(sid, excludedSids))
                continue;

            AccumulateRights(allowed, denied, sid.Value, rule.AccessControlType, rule.RegistryRights);
        }

        return SubtractDenied(allowed, denied);
    }

    // Callers only instantiate TRights as FileSystemRights or RegistryRights — both int-sized enums.
    // Unsafe.As<TRights, int> is safe because sizeof(TRights) == sizeof(int) in both cases.
    private static void AccumulateRights<TRights>(
        Dictionary<string, TRights> allowed, Dictionary<string, TRights> denied,
        string sidStr, AccessControlType type, TRights rights)
        where TRights : struct, Enum
    {
        var target = type == AccessControlType.Allow ? allowed : denied;
        target.TryGetValue(sidStr, out var existing);
        var combined = Unsafe.As<TRights, int>(ref existing) | Unsafe.As<TRights, int>(ref rights);
        target[sidStr] = Unsafe.As<int, TRights>(ref combined);
    }

    private static Dictionary<string, TRights> SubtractDenied<TRights>(
        Dictionary<string, TRights> allowed, Dictionary<string, TRights> denied)
        where TRights : struct, Enum
    {
        var effective = new Dictionary<string, TRights>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sidStr, allowedRights) in allowed)
        {
            denied.TryGetValue(sidStr, out var deniedRights);
            var a = allowedRights;
            var d = deniedRights;
            var net = Unsafe.As<TRights, int>(ref a) & ~Unsafe.As<TRights, int>(ref d);
            if (net != 0)
                effective[sidStr] = Unsafe.As<int, TRights>(ref net);
        }

        return effective;
    }

    public static bool IsTrustedSystemSid(SecurityIdentifier sid)
    {
        if (sid.Equals(AdministratorsSid) || sid.Equals(LocalSystemSid) ||
            sid.Equals(CreatorOwnerSid) || sid.Equals(TrustedInstallerSid))
            return true;

        // Service identity and service account SIDs — not exploitable by regular users
        var sidStr = sid.Value;
        if (sidStr.StartsWith("S-1-5-80-", StringComparison.Ordinal))
            return true; // NT SERVICE\*
        if (sidStr.StartsWith("S-1-5-87-", StringComparison.Ordinal))
            return true; // Virtual/task identity
        if (sidStr.StartsWith("S-1-5-99-", StringComparison.Ordinal))
            return true; // RESTRICTED SERVICES\*
        if (sidStr is "S-1-5-19" or "S-1-5-20")
            return true; // LOCAL SERVICE / NETWORK SERVICE

        // Well-known admin RIDs in any domain/machine (S-1-5-21-...-RID).
        // These are inherently admin-level and never regular users, even when the DC
        // is unreachable and GetAdminMemberSids can't enumerate them.
        if (sidStr.StartsWith("S-1-5-21-", StringComparison.Ordinal))
        {
            var lastDash = sidStr.LastIndexOf('-');
            if (lastDash > 0)
            {
                var rid = sidStr.AsSpan(lastDash + 1);
                if (rid is "500" or "512" or "519") // Administrator, Domain Admins, Enterprise Admins
                    return true;
            }
        }

        return false;
    }

    public static bool IsTrustedSid(SecurityIdentifier sid, string? ownerSid)
    {
        if (IsTrustedSystemSid(sid))
            return true;

        if (ownerSid != null)
        {
            try
            {
                var ownerSecId = new SecurityIdentifier(ownerSid);
                if (sid.Equals(ownerSecId))
                    return true;
            }
            catch
            {
                /* invalid owner SID format — don't exclude */
            }
        }

        return false;
    }

    private static bool IsTrustedSid(SecurityIdentifier sid, HashSet<string> excludedSids)
    {
        if (IsTrustedSystemSid(sid))
            return true;

        return excludedSids.Contains(sid.Value);
    }
}