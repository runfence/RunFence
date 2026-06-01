using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Acl;

/// <summary>
/// Low-level NTFS ACE operations for grant entries.
/// </summary>
public class GrantAceService(
    IPathSecurityDescriptorAccessor securityAccessor,
    IExplicitAceAccessor explicitAceAccessor,
    IFileSystemPathInfo pathInfo) : IGrantAceService
{
    public FileSystemSecurity GetSecurity(string path) => securityAccessor.GetSecurity(path);

    public void ApplyAce(string path, string sid, bool isDeny, SavedRightsState rights, bool isFolder)
    {
        var fsRights = isDeny
            ? GrantRightsMapper.MapDenyRights(rights, isFolder)
            : GrantRightsMapper.MapAllowRights(rights, isFolder);
        explicitAceAccessor.ApplyExplicitAce(path, sid, isDeny ? AccessControlType.Deny : AccessControlType.Allow, fsRights);
    }

    public void RevertAce(string path, string sid, bool isDeny)
    {
        var type = isDeny ? AccessControlType.Deny : AccessControlType.Allow;
        Func<FileSystemAccessRule, bool>? shouldSkip = type == AccessControlType.Allow
            ? rule => rule.FileSystemRights == TraverseRightsHelper.TraverseRights &&
                      rule.InheritanceFlags == InheritanceFlags.None
            : null;
        explicitAceAccessor.RemoveExplicitAces(path, sid, type, shouldSkip);
    }

    public GrantRightsState ReadGrantState(string path, string sid, IReadOnlyList<string> groupSids)
    {
        bool isFolder = pathInfo.DirectoryExists(path);
        var security = securityAccessor.GetSecurity(path);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));

        FileSystemRights allowDirect = 0;
        FileSystemRights denyDirect = 0;
        FileSystemRights allowGroup = 0;
        FileSystemRights denyGroup = 0;
        bool directTraverseAllow = false;
        bool groupTraverseAllow = false;
        bool directTraverseDeny = false;
        bool groupTraverseDeny = false;
        int directAllowAceCount = 0;
        int directDenyAceCount = 0;

        var identity = new SecurityIdentifier(sid);
        var groupIdentities = groupSids.Select(g => new SecurityIdentifier(g)).ToList();

        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.IdentityReference is not SecurityIdentifier ruleSid)
                continue;
            bool isDirect = ruleSid.Equals(identity);
            bool isGroup = !isDirect && groupIdentities.Any(g => g.Equals(ruleSid));
            if (!isDirect && !isGroup)
                continue;

            if (rule.AccessControlType == AccessControlType.Allow)
            {
                if (IsTraverseOnlyAce(rule))
                {
                    if (isDirect)
                        directTraverseAllow = true;
                    else
                        groupTraverseAllow = true;
                    continue;
                }

                if (isDirect)
                {
                    allowDirect |= rule.FileSystemRights;
                    directAllowAceCount++;
                }
                else
                    allowGroup |= rule.FileSystemRights;
            }
            else
            {
                if (IsTraverseOnlyAce(rule))
                {
                    if (isDirect)
                        directTraverseDeny = true;
                    else
                        groupTraverseDeny = true;
                    continue;
                }

                if (isDirect)
                {
                    denyDirect |= rule.FileSystemRights;
                    directDenyAceCount++;
                }
                else
                    denyGroup |= rule.FileSystemRights;
            }
        }

        var ownerIdentity = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        bool isAdminOwner = ownerIdentity != null && ownerIdentity.Equals(adminsSid);
        RightCheckState isAccountOwner;
        if (ownerIdentity != null && ownerIdentity.Equals(identity))
            isAccountOwner = RightCheckState.Checked;
        else if (ownerIdentity != null && groupIdentities.Any(g => g.Equals(ownerIdentity)))
            isAccountOwner = RightCheckState.Indeterminate;
        else
            isAccountOwner = RightCheckState.Unchecked;

        var writeMask = isFolder ? GrantRightsMapper.WriteFolderMask : GrantRightsMapper.WriteFileMask;
        var specialMask = isFolder ? GrantRightsMapper.SpecialFolderMask : GrantRightsMapper.SpecialFileMask;

        return new GrantRightsState(
            AllowExecute: GetCheckState(GrantRightsMapper.ExecuteMask, allowDirect, allowGroup),
            AllowWrite: GetCheckState(writeMask, allowDirect, allowGroup),
            AllowSpecial: GetCheckState(specialMask, allowDirect, allowGroup),
            DenyRead: GetCheckState(GrantRightsMapper.ReadMask, denyDirect, denyGroup),
            DenyExecute: GetCheckState(GrantRightsMapper.ExecuteMask, denyDirect, denyGroup),
            DenyWrite: GetCheckState(writeMask, denyDirect, denyGroup),
            DenySpecial: GetCheckState(specialMask, denyDirect, denyGroup),
            TraverseOnlyAllow: GetTraverseOnlyCheckState(directTraverseAllow, groupTraverseAllow),
            TraverseOnlyDeny: GetTraverseOnlyCheckState(directTraverseDeny, groupTraverseDeny),
            IsAccountOwner: isAccountOwner,
            IsAdminOwner: isAdminOwner,
            DirectAllowAceCount: directAllowAceCount,
            DirectDenyAceCount: directDenyAceCount);
    }

    public PathAclStatus CheckGrantStatus(string path, string sid, bool isDeny)
    {
        if (!pathInfo.FileExists(path) && !pathInfo.DirectoryExists(path))
            return PathAclStatus.Unavailable;

        try
        {
            var security = securityAccessor.GetSecurity(path);
            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
            var identity = new SecurityIdentifier(sid);

            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.IdentityReference is not SecurityIdentifier ruleSid)
                    continue;
                if (!ruleSid.Equals(identity))
                    continue;
                bool matches = isDeny
                    ? rule.AccessControlType == AccessControlType.Deny
                    : rule.AccessControlType == AccessControlType.Allow;
                if (matches && !IsTraverseOnlyAce(rule))
                    return PathAclStatus.Available;
            }

            return PathAclStatus.Broken;
        }
        catch
        {
            return PathAclStatus.Broken;
        }
    }

    private static RightCheckState GetCheckState(FileSystemRights mask, FileSystemRights direct, FileSystemRights group)
    {
        if ((direct & mask) == mask)
            return RightCheckState.Checked;
        if ((group & mask) == mask)
            return RightCheckState.Indeterminate;
        return RightCheckState.Unchecked;
    }

    private static RightCheckState GetTraverseOnlyCheckState(bool direct, bool group)
    {
        if (direct)
            return RightCheckState.Checked;
        if (group)
            return RightCheckState.Indeterminate;
        return RightCheckState.Unchecked;
    }

    private static bool IsTraverseOnlyAce(FileSystemAccessRule rule)
        => GrantRightsMapper.IsTraverseOnly(rule.FileSystemRights) &&
           rule.InheritanceFlags == InheritanceFlags.None;
}
