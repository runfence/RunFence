using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Acl;

/// <summary>
/// Low-level NTFS ACE operations (apply, revert, read, check, ownership) extracted from
/// <see cref="PathGrantService"/> to keep that class focused on orchestration.
/// All operations are direct NTFS reads/writes with no mutable state.
/// </summary>
public class GrantNtfsHelper(IAclAccessor acl, ILoggingService log) : IGrantNtfsHelper
{
    public FileSystemSecurity GetSecurity(string path) => acl.GetSecurity(path);

    public bool PathExists(string path, out bool isFolder)
        => acl.PathExists(path, out isFolder);

    public void ApplyAce(string path, string sid, bool isDeny, SavedRightsState rights, bool isFolder)
    {
        var fsRights = isDeny
            ? GrantRightsMapper.MapDenyRights(rights, isFolder)
            : GrantRightsMapper.MapAllowRights(rights, isFolder);
        acl.ApplyExplicitAce(path, sid, isDeny ? AccessControlType.Deny : AccessControlType.Allow, fsRights);
    }

    public void RevertAce(string path, string sid, bool isDeny)
    {
        acl.RemoveExplicitAces(path, sid, isDeny ? AccessControlType.Deny : AccessControlType.Allow);
    }

    public GrantRightsState ReadGrantState(string path, string sid, IReadOnlyList<string> groupSids)
    {
        bool isFolder = Directory.Exists(path);
        var security = acl.GetSecurity(path);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));

        FileSystemRights allowDirect = 0;
        FileSystemRights denyDirect = 0;
        FileSystemRights allowGroup = 0;
        FileSystemRights denyGroup = 0;
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
                if (isDirect)
                {
                    if (GrantRightsMapper.IsTraverseOnly(rule.FileSystemRights) &&
                        rule.InheritanceFlags == InheritanceFlags.None)
                        continue;
                    allowDirect |= rule.FileSystemRights;
                    directAllowAceCount++;
                }
                else
                    allowGroup |= rule.FileSystemRights;
            }
            else
            {
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
            IsAccountOwner: isAccountOwner,
            IsAdminOwner: isAdminOwner,
            DirectAllowAceCount: directAllowAceCount,
            DirectDenyAceCount: directDenyAceCount);
    }

    public PathAclStatus CheckGrantStatus(string path, string sid, bool isDeny)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return PathAclStatus.Unavailable;

        try
        {
            var security = acl.GetSecurity(path);
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
                if (matches)
                    return PathAclStatus.Available;
            }

            return PathAclStatus.Broken;
        }
        catch
        {
            return PathAclStatus.Broken;
        }
    }

    public void ChangeOwner(string path, string sid, bool recursive)
    {
        var ownerSid = new SecurityIdentifier(sid);
        SetOwnerInternal(path, ownerSid);
        if (recursive && Directory.Exists(path))
            RecursiveSetOwner(path, ownerSid);
    }

    public void ResetOwner(string path, bool recursive)
    {
        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        SetOwnerInternal(path, adminsSid);
        if (recursive && Directory.Exists(path))
            RecursiveSetOwner(path, adminsSid);
    }

    private void SetOwnerInternal(string path, SecurityIdentifier ownerSid)
    {
        if (Directory.Exists(path))
        {
            var dirInfo = new DirectoryInfo(path);
            var security = dirInfo.GetAccessControl(AccessControlSections.Owner);
            security.SetOwner(ownerSid);
            dirInfo.SetAccessControl(security);
        }
        else
        {
            var fileInfo = new FileInfo(path);
            var security = fileInfo.GetAccessControl(AccessControlSections.Owner);
            security.SetOwner(ownerSid);
            fileInfo.SetAccessControl(security);
        }
    }

    private void RecursiveSetOwner(string dirPath, SecurityIdentifier ownerSid)
    {
        string[]? subDirs = null;
        try { subDirs = Directory.GetDirectories(dirPath); }
        catch (Exception ex) { log.Warn($"Failed to enumerate directories in '{dirPath}': {ex.Message}"); }

        if (subDirs != null)
        {
            foreach (var subDir in subDirs)
            {
                try
                {
                    if ((File.GetAttributes(subDir) & FileAttributes.ReparsePoint) != 0)
                        continue;
                }
                catch { continue; }
                TrySetOwner(subDir, ownerSid);
                RecursiveSetOwner(subDir, ownerSid);
            }
        }

        string[]? files = null;
        try { files = Directory.GetFiles(dirPath); }
        catch (Exception ex) { log.Warn($"Failed to enumerate files in '{dirPath}': {ex.Message}"); }

        if (files != null)
        {
            foreach (var file in files)
                TrySetOwner(file, ownerSid);
        }
    }

    private void TrySetOwner(string path, SecurityIdentifier ownerSid)
    {
        try { SetOwnerInternal(path, ownerSid); }
        catch (Exception ex) { log.Warn($"Failed to set owner on '{path}': {ex.Message}"); }
    }

    private static RightCheckState GetCheckState(FileSystemRights mask, FileSystemRights direct, FileSystemRights group)
    {
        if ((direct & mask) == mask)
            return RightCheckState.Checked;
        if ((group & mask) == mask)
            return RightCheckState.Indeterminate;
        return RightCheckState.Unchecked;
    }
}
