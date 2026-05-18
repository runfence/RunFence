using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Infrastructure;

namespace RunFence.Acl.Permissions;

/// <summary>
/// Injectable service for ACL permission checks and write operations: restricting access
/// and querying effective permissions.
/// </summary>
public class AclPermissionService(
    NTTranslateApi ntTranslate,
    GroupMembershipApi groupMembership,
    ILocalGroupQueryService localGroupQueryService,
    IAclAccessor aclAccessor,
    IAclAccessEvaluator accessEvaluator) : IAclPermissionService
{
    public bool HasEffectiveRights(
        FileSystemSecurity security,
        string accountSid,
        IReadOnlyList<string> accountGroupSids,
        FileSystemRights requiredRights)
    {
        var evaluation = accessEvaluator.Evaluate(security, accountSid, accountGroupSids, requiredRights);
        if (evaluation.Status == AclAccessEvaluationStatus.Failed)
            return false;
        return (evaluation.GrantedRights & requiredRights) == requiredRights &&
               (evaluation.DeniedRights & requiredRights) == 0;
    }

    public List<string> ResolveAccountGroupSids(string accountSid)
    {
        // AppContainer SIDs (S-1-15-2-*) and the Low Integrity label SID (S-1-16-4096) are not
        // user accounts — they have a fixed, well-known group membership and NetUserGetLocalGroups
        // does not apply to them.
        if (AclHelper.IsContainerSid(accountSid) || AclHelper.IsLowIntegritySid(accountSid))
            return ["S-1-1-0", AclHelper.AllApplicationPackagesSid]; // Everyone + ALL_APPLICATION_PACKAGES

        var sids = new List<string>
        {
            "S-1-1-0", // Everyone
            "S-1-5-11", // Authenticated Users
            // BUILTIN\Users (S-1-5-32-545) is hardcoded because it is invariably present in every
            // authenticated user's token: LSA unconditionally injects Authenticated Users (S-1-5-11)
            // into every token, and Authenticated Users is always a member of BUILTIN\Users, so
            // SamrGetAliasMembership always resolves it — regardless of explicit SAM group membership.
            // Removing the account from BUILTIN\Users via NetLocalGroupDelMembers has no effect on
            // the token. Windows also auto-restores Authenticated Users membership on reboot.
            "S-1-5-32-545", // BUILTIN\Users
        };

        foreach (var groupSid in TryResolveLocalGroupSids(accountSid))
        {
            if (!sids.Contains(groupSid, StringComparer.OrdinalIgnoreCase))
                sids.Add(groupSid);
        }

        return sids;
    }

    private IEnumerable<string> TryResolveLocalGroupSids(string accountSid)
    {
        try
        {
            if (localGroupQueryService.IsLocalGroup(accountSid))
                return [];

            var ntAccount = ntTranslate.TranslateName(accountSid);

            var netApiResult = groupMembership.NetUserGetLocalGroups(ntAccount.Value);

            if (netApiResult.ReturnCode != 0 || netApiResult.BufPtr == IntPtr.Zero)
                return [];

            try
            {
                var result = new List<string>();
                var structSize = Marshal.SizeOf<GroupMembershipNative.LOCALGROUP_USERS_INFO_0>();
                for (var i = 0; i < netApiResult.EntriesRead; i++)
                {
                    var entry = Marshal.PtrToStructure<GroupMembershipNative.LOCALGROUP_USERS_INFO_0>(
                        IntPtr.Add(netApiResult.BufPtr, i * structSize));
                    try
                    {
                        var sid = ntTranslate.TranslateSid(entry.lgrui0_name);
                        result.Add(sid.Value);
                    }
                    catch
                    {
                    } // group name may not resolve — skip
                }

                return result;
            }
            finally
            {
                GroupMembershipNative.NetApiBufferFree(netApiResult.BufPtr);
            }
        }
        catch
        {
            return [];
        }
    }

    public bool NeedsPermissionGrant(string filePath, string accountSid,
        FileSystemRights requiredRights = FileSystemRights.ReadAndExecute, bool unelevated = false)
    {
        try
        {
            FileSystemSecurity fileSecurity = aclAccessor.GetSecurity(filePath);
            var groupSids = ResolveAccountGroupSids(accountSid);
            if (unelevated)
                groupSids.RemoveAll(s =>
                    string.Equals(s, AclComputeHelper.AdministratorsSid.Value, StringComparison.OrdinalIgnoreCase));
            return !HasEffectiveRights(fileSecurity, accountSid, groupSids, requiredRights);
        }
        catch
        {
            // On error, assume the grant is needed — callers will attempt the grant which fails
            // clearly, rather than silently skipping a needed permission.
            return true;
        }
    }

    /// <summary>
    /// Returns true if the account needs ReadAndExecute on the directory containing
    /// <paramref name="filePath"/> (or <paramref name="filePath"/> itself if it is a directory).
    /// </summary>
    public bool NeedsPermissionGrantOrParent(string filePath, string accountSid)
    {
        var directory = Directory.Exists(filePath) ? filePath : Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory) || PathHelper.IsBlockedAclPath(directory))
            return false;
        return NeedsPermissionGrant(directory, accountSid);
    }

    /// <summary>
    /// Returns the grantable ancestor directories of <paramref name="filePath"/>, from most-specific
    /// to least-specific. Starts at the immediate parent (or <paramref name="filePath"/> itself if it
    /// is a directory). Stops before any <see cref="PathHelper.IsBlockedAclRoot"/> path and before
    /// drive roots (directories with no parent). Returns an empty list if the immediate parent is
    /// itself a blocked root.
    /// </summary>
    public IReadOnlyList<string> GetGrantableAncestors(string filePath)
    {
        var start = Directory.Exists(filePath) ? filePath : Path.GetDirectoryName(filePath);
        var result = new List<string>();
        var current = start;

        while (!string.IsNullOrEmpty(current))
        {
            // Stop before blocked ACL roots (exact match — children are still grantable)
            if (PathHelper.IsBlockedAclRoot(current))
                break;

            // Stop before drive roots (directories that have no parent)
            if (Directory.GetParent(current) == null)
                break;

            result.Add(current);
            current = Path.GetDirectoryName(current);
        }

        return result;
    }

    /// <summary>
    /// Disables inheritance on a file and restricts access to SYSTEM (FullControl) and
    /// Administrators (FullControl). In admin-operation mock mode, the current process SID
    /// also gets FullControl so the non-elevated debug process can still access the file.
    /// All other ACEs are removed.
    /// </summary>
    public void RestrictToAdmins(string filePath)
    {
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

        var fileInfo = new FileInfo(filePath);
        var security = fileInfo.GetAccessControl();
        security.SetAccessRuleProtection(true, false);

        var existingRules = security.GetAccessRules(true, false, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        foreach (var rule in existingRules)
            security.RemoveAccessRuleSpecific(rule);

        security.AddAccessRule(new FileSystemAccessRule(systemSid, FileSystemRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(adminsSid, FileSystemRights.FullControl, AccessControlType.Allow));
        AdminOperationMockAccessHelper.AddCurrentProcessFileSystemAccess(security, FileSystemRights.FullControl);
        fileInfo.SetAccessControl(security);
    }
}
