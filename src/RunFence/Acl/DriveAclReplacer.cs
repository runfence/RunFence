using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;

namespace RunFence.Acl;

/// <summary>
/// Replaces broad-access ACEs on drive roots for the Prepare System wizard template.
/// For each target drive, removes ACEs whose trustee is Users (S-1-5-32-545),
/// Authenticated Users (S-1-5-11), or Everyone (S-1-1-0) and adds equivalent ACEs (same rights
/// and inheritance/propagation flags) for the specified target SID. If the drive owner is one
/// of those three groups, ownership is transferred to the target SID.
/// Requires <c>SeBackupPrivilege</c>, <c>SeRestorePrivilege</c>, and
/// <c>SeTakeOwnershipPrivilege</c> — enabled at startup in <c>Program.cs</c>.
/// </summary>
public class DriveAclReplacer(IPathGrantService pathGrantService, ILoggingService log)
{
    private static readonly SecurityIdentifier UsersSid =
        new(WellKnownSidType.BuiltinUsersSid, null);

    private static readonly SecurityIdentifier AuthenticatedUsersSid =
        new(WellKnownSidType.AuthenticatedUserSid, null);

    private static readonly SecurityIdentifier EveryoneSid =
        new(WellKnownSidType.WorldSid, null);

    /// <summary>
    /// Replaces broad-access ACEs on <paramref name="drivePath"/> for the given <paramref name="targetSid"/>.
    /// Non-fatal errors are returned as a string; null means success.
    /// </summary>
    public string? ReplaceDriveAcl(string drivePath, string targetSid)
    {
        try
        {
            var security = new DirectoryInfo(drivePath).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            var targetIdentity = new SecurityIdentifier(targetSid);

            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();

            // Build a HashSet of existing target-SID rules keyed by (maskedRights, InheritanceFlags, PropagationFlags, AccessControlType)
            // for O(1) duplicate checks in the loop below.
            var existingTargetRuleKeys = new HashSet<(FileSystemRights, InheritanceFlags, PropagationFlags, AccessControlType)>(
                rules
                    .Where(r => r.IdentityReference.Equals(targetIdentity))
                    .Select(r => (r.FileSystemRights & FileSystemRights.FullControl, r.InheritanceFlags, r.PropagationFlags, r.AccessControlType)));

            bool changed = false;

            foreach (var rule in rules)
            {
                if (!IsBroadAccessSid((SecurityIdentifier)rule.IdentityReference))
                    continue;

                security.RemoveAccessRuleSpecific(rule);

                // Mask out generic-rights bits (GENERIC_READ/WRITE/EXECUTE/ALL = 0xF0000000)
                // which are not part of the FileSystemRights enum and cause InvalidEnumArgumentException
                // in the FileSystemAccessRule constructor. Drive root ACEs inherited from older OS
                // installs or third-party software occasionally carry these non-standard bits.
                var maskedRights = rule.FileSystemRights & FileSystemRights.FullControl;

                if (maskedRights == 0)
                {
                    log.Warn($"DriveAclReplacer: ACE on {drivePath} has no standard FileSystemRights bits (raw: 0x{(int)rule.FileSystemRights:X8}) — removed broad ACE without replacement");
                    changed = true;
                    continue;
                }

                // Add equivalent ACE for the target SID, unless an identical ACE already exists.
                var key = (maskedRights, rule.InheritanceFlags, rule.PropagationFlags, rule.AccessControlType);
                if (!existingTargetRuleKeys.Contains(key))
                {
                    security.AddAccessRule(new FileSystemAccessRule(
                        targetIdentity,
                        maskedRights,
                        rule.InheritanceFlags,
                        rule.PropagationFlags,
                        rule.AccessControlType));
                    existingTargetRuleKeys.Add(key);
                }

                changed = true;
            }

            // Transfer ownership if the current owner is one of the broad-access groups.
            var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            if (owner != null && IsBroadAccessSid(owner))
            {
                security.SetOwner(targetIdentity);
                changed = true;
            }

            if (changed)
                new DirectoryInfo(drivePath).SetAccessControl(security);

            // Sync DB with actual NTFS state — reads back ACEs we (or other code) just wrote.
            pathGrantService.UpdateFromPath(drivePath, targetSid);

            return null;
        }
        catch (Exception ex)
        {
            log.Error($"DriveAclReplacer: failed to replace ACL on {drivePath}", ex);
            return ex.Message;
        }
    }

    /// <summary>
    /// Returns true when the drive root at <paramref name="drivePath"/> has at least one explicit ACE
    /// for a broad-access group (Users, Authenticated Users, or Everyone).
    /// Used by the wizard to decide whether Prepare System is applicable to a drive.
    /// </summary>
    public bool HasReplaceableBroadAces(string drivePath)
    {
        try
        {
            var rules = new DirectoryInfo(drivePath)
                .GetAccessControl(AccessControlSections.Access)
                .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
            return rules.Cast<FileSystemAccessRule>().Any(rule => IsBroadAccessSid((SecurityIdentifier)rule.IdentityReference));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsBroadAccessSid(SecurityIdentifier sid) =>
        sid.Equals(UsersSid) ||
        sid.Equals(AuthenticatedUsersSid) ||
        sid.Equals(EveryoneSid);
}