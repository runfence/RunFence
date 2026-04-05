using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Core.Models;

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
public class DriveAclReplacer(IPermissionGrantService permissionGrantService, ILoggingService log)
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
    public string? ReplaceDriveAcl(string drivePath, string targetSid, SavedRightsState savedRights)
    {
        try
        {
            var security = new DirectoryInfo(drivePath).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
            var targetIdentity = new SecurityIdentifier(targetSid);

            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>()
                .ToList();

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
                var existingRules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
                    .Cast<FileSystemAccessRule>();

                bool duplicate = existingRules.Any(r =>
                    r.IdentityReference.Equals(targetIdentity) &&
                    (r.FileSystemRights & FileSystemRights.FullControl) == maskedRights &&
                    r.InheritanceFlags == rule.InheritanceFlags &&
                    r.PropagationFlags == rule.PropagationFlags &&
                    r.AccessControlType == rule.AccessControlType);

                if (!duplicate)
                {
                    security.AddAccessRule(new FileSystemAccessRule(
                        targetIdentity,
                        maskedRights,
                        rule.InheritanceFlags,
                        rule.PropagationFlags,
                        rule.AccessControlType));
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

            // Record in AccountGrants regardless of whether ACEs were changed
            // (the wizard already applied the ACLs directly; we just track what was done).
            permissionGrantService.RecordGrantWithRights(drivePath, targetSid, savedRights);

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