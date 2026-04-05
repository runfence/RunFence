using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Acl;

/// <summary>
/// Implementation of IGrantedPathAclService. Reads/writes explicit NTFS ACEs.
/// SeBackupPrivilege, SeRestorePrivilege, and SeTakeOwnershipPrivilege are enabled once at
/// startup (Program.cs) for the lifetime of the elevated admin process — no per-call enable needed.
/// </summary>
public class GrantedPathAclService(ILoggingService log) : IGrantedPathAclService
{
    // Allow-mode rights covered by each checkbox
    public const FileSystemRights ReadRightsMask = FileSystemRights.ReadData | FileSystemRights.ReadAttributes | FileSystemRights.ReadExtendedAttributes | FileSystemRights.ReadPermissions | FileSystemRights.Synchronize;

    public const FileSystemRights ExecuteRightsMask = FileSystemRights.ExecuteFile;

    public const FileSystemRights WriteRightsMask = FileSystemRights.WriteData | FileSystemRights.AppendData | FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles;

    public const FileSystemRights SpecialRightsMask = FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

    public GrantRightsState ReadRights(string path, string sid, IReadOnlyList<string> groupSids)
    {
        FileSystemSecurity security = NativeAclAccessor.GetSecurity(path);
        var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));

        FileSystemRights allowDirect = 0;
        FileSystemRights denyDirect = 0;
        FileSystemRights allowGroup = 0;
        FileSystemRights denyGroup = 0;
        int directAllowAceCount = 0;
        int directDenyAceCount = 0;

        var identity = new SecurityIdentifier(sid);
        var groupIdentities = groupSids
            .Select(g => new SecurityIdentifier(g))
            .ToList();

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
                    // Skip traverse-only ACEs — managed by the traverse system, not the grant system.
                    // They must not affect the grant rights count or rights accumulation.
                    if (rule.FileSystemRights == TraverseRightsHelper.TraverseRights &&
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

        // Determine owner
        var ownerIdentity = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        bool isAdminOwner = ownerIdentity != null && ownerIdentity.Equals(adminsSid);
        CheckState isAccountOwner;
        if (ownerIdentity != null && ownerIdentity.Equals(identity))
            isAccountOwner = CheckState.Checked;
        else if (ownerIdentity != null && groupIdentities.Any(g => g.Equals(ownerIdentity)))
            isAccountOwner = CheckState.Indeterminate;
        else
            isAccountOwner = CheckState.Unchecked;

        return new GrantRightsState(
            AllowExecute: GetCheckState(ExecuteRightsMask, allowDirect, allowGroup),
            AllowWrite: GetCheckState(WriteRightsMask, allowDirect, allowGroup),
            AllowSpecial: GetCheckState(SpecialRightsMask, allowDirect, allowGroup),
            DenyRead: GetCheckState(ReadRightsMask, denyDirect, denyGroup),
            DenyExecute: GetCheckState(ExecuteRightsMask, denyDirect, denyGroup),
            DenyWrite: GetCheckState(WriteRightsMask, denyDirect, denyGroup),
            DenySpecial: GetCheckState(SpecialRightsMask, denyDirect, denyGroup),
            IsAccountOwner: isAccountOwner,
            IsAdminOwner: isAdminOwner,
            DirectAllowAceCount: directAllowAceCount,
            DirectDenyAceCount: directDenyAceCount);
    }

    public PathAclStatus CheckPathStatus(string path, string sid, bool isDeny)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return PathAclStatus.Unavailable;

        try
        {
            FileSystemSecurity security = NativeAclAccessor.GetSecurity(path);
            var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));
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

    public void ApplyAllowRights(string path, string sid, AllowRights rights)
    {
        var combinedRights = ReadRightsMask; // Read always on
        if (rights.Execute)
            combinedRights |= ExecuteRightsMask;
        if (rights.Write)
            combinedRights |= WriteRightsMask;
        if (rights.Special)
            combinedRights |= SpecialRightsMask;

        NativeAclAccessor.ApplyExplicitAce(path, sid, AccessControlType.Allow, combinedRights);
    }

    public void ApplyDenyRights(string path, string sid, DenyRights rights)
    {
        var combinedRights = WriteRightsMask | SpecialRightsMask; // Write+Special always on
        if (rights.Read)
            combinedRights |= ReadRightsMask;
        if (rights.Execute)
            combinedRights |= ExecuteRightsMask;

        NativeAclAccessor.ApplyExplicitAce(path, sid, AccessControlType.Deny, combinedRights);
    }

    public void ApplyReadOnlyGrant(string path, string sid)
    {
        NativeAclAccessor.ApplyExplicitAce(path, sid, AccessControlType.Allow, ReadRightsMask);
    }

    public void RevertGrant(string path, string sid, bool isDeny)
    {
        NativeAclAccessor.RemoveExplicitAces(path, sid, isDeny ? AccessControlType.Deny : AccessControlType.Allow);
    }

    public void RevertAllGrants(string path, string sid)
    {
        NativeAclAccessor.RemoveExplicitAces(path, sid, AccessControlType.Allow);
        NativeAclAccessor.RemoveExplicitAces(path, sid, AccessControlType.Deny);
    }

    public void RevertAllGrantsBatch(IEnumerable<GrantedPathEntry> grants, string sid)
    {
        var entries = grants.Where(e => !e.IsTraverseOnly).ToList();
        if (entries.Count == 0)
            return;
        foreach (var entry in entries)
        {
            try
            {
                NativeAclAccessor.RemoveExplicitAces(entry.Path, sid, AccessControlType.Allow);
                NativeAclAccessor.RemoveExplicitAces(entry.Path, sid, AccessControlType.Deny);
            }
            catch (Exception ex)
            {
                log.Warn($"Failed to revert grant on '{entry.Path}' for SID '{sid}': {ex.Message}");
            }
        }
    }

    public void ChangeOwner(string path, string sid, bool recursive)
    {
        var identity = new SecurityIdentifier(sid);
        SetOwnerInternal(path, identity);
        if (recursive && Directory.Exists(path))
            RecursiveSetOwner(path, identity);
    }

    public void ResetOwner(string path, bool recursive)
    {
        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        SetOwnerInternal(path, adminsSid);
        if (recursive && Directory.Exists(path))
            RecursiveSetOwner(path, adminsSid);
    }

    private void RecursiveSetOwner(string dirPath, SecurityIdentifier ownerSid)
    {
        string[]? subDirs = null;
        try
        {
            subDirs = Directory.GetDirectories(dirPath);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to enumerate directories in '{dirPath}': {ex.Message}");
        }

        if (subDirs != null)
        {
            foreach (var subDir in subDirs)
            {
                try
                {
                    if ((File.GetAttributes(subDir) & FileAttributes.ReparsePoint) != 0)
                        continue;
                }
                catch
                {
                    continue;
                }

                TrySetOwner(subDir, ownerSid);
                RecursiveSetOwner(subDir, ownerSid);
            }
        }

        string[]? files = null;
        try
        {
            files = Directory.GetFiles(dirPath);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to enumerate files in '{dirPath}': {ex.Message}");
        }

        if (files != null)
        {
            foreach (var file in files)
                TrySetOwner(file, ownerSid);
        }
    }

    private static CheckState GetCheckState(FileSystemRights mask, FileSystemRights direct, FileSystemRights group)
    {
        if ((direct & mask) == mask)
            return CheckState.Checked;
        if ((group & mask) == mask)
            return CheckState.Indeterminate;
        return CheckState.Unchecked;
    }

    private static void SetOwnerInternal(string path, SecurityIdentifier ownerSid)
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

    private void TrySetOwner(string path, SecurityIdentifier ownerSid)
    {
        try
        {
            SetOwnerInternal(path, ownerSid);
        }
        catch (Exception ex)
        {
            log.Warn($"Failed to set owner on '{path}': {ex.Message}");
        }
    }
}