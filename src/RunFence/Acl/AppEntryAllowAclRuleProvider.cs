using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;
using RunFence.Core.Models;

namespace RunFence.Acl;

public sealed class AppEntryAllowAclRuleProvider(
    IAppEntryAclTargetResolver aclTargetResolver)
{
    public ManagedAclRuleSet BuildAllowModeRuleSet(AppEntry app, bool isDirectory)
    {
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var adminsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var inheritanceFlags = AclHelper.InheritanceFlagsFor(isDirectory);
        const PropagationFlags propagationFlags = PropagationFlags.None;
        var rules = new List<FileSystemAccessRule>
        {
            new(
                systemSid,
                FileSystemRights.FullControl,
                inheritanceFlags,
                propagationFlags,
                AccessControlType.Allow),
            new(
                adminsSid,
                FileSystemRights.ChangePermissions | FileSystemRights.ReadPermissions |
                FileSystemRights.ReadAttributes | FileSystemRights.ReadExtendedAttributes,
                inheritanceFlags,
                propagationFlags,
                AccessControlType.Allow)
        };
        var managedSids = new HashSet<SecurityIdentifier> { systemSid, adminsSid };
        var invalidEntries = new List<InvalidAllowAclEntryRule>();

        var currentMockSid = AdminOperationMockAccessHelper.GetCurrentProcessSidWhenUsingMocks();
        if (currentMockSid != null)
        {
            rules.Add(new FileSystemAccessRule(
                currentMockSid,
                FileSystemRights.ChangePermissions | FileSystemRights.ReadPermissions |
                FileSystemRights.ReadAttributes | FileSystemRights.ReadExtendedAttributes,
                inheritanceFlags,
                propagationFlags,
                AccessControlType.Allow));
            managedSids.Add(currentMockSid);
        }

        if (app.AllowedAclEntries != null)
        {
            foreach (var entry in app.AllowedAclEntries)
            {
                try
                {
                    var sid = new SecurityIdentifier(entry.Sid);
                    rules.Add(new FileSystemAccessRule(
                        sid,
                        BuildEntryRights(entry),
                        inheritanceFlags,
                        propagationFlags,
                        AccessControlType.Allow));
                    managedSids.Add(sid);
                }
                catch (Exception ex)
                {
                    invalidEntries.Add(new InvalidAllowAclEntryRule(entry.Sid, ex));
                }
            }
        }

        return new ManagedAclRuleSet(rules, managedSids, invalidEntries);
    }

    public Dictionary<string, FileSystemRights> BuildAllowManagedRightsBySid(
        string normalizedPath,
        bool isDirectory,
        IReadOnlyList<AppEntry> apps)
    {
        var allowManagedRights = new Dictionary<string, FileSystemRights>(StringComparer.OrdinalIgnoreCase);

        foreach (var app in apps)
        {
            if (app.AclMode != AclMode.Allow)
                continue;
            if (app.AllowedAclEntries == null || app.AllowedAclEntries.Count == 0)
                continue;

            var appTargetPath = AclHelper.NormalizePath(aclTargetResolver.ResolveTargetPath(app));
            if (!string.Equals(appTargetPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                continue;

            var ruleSet = BuildAllowModeRuleSet(app, isDirectory);
            foreach (var rule in ruleSet.Rules)
            {
                var sid = ((SecurityIdentifier)rule.IdentityReference).Value;
                allowManagedRights.TryGetValue(sid, out var existingRights);
                allowManagedRights[sid] = existingRights | rule.FileSystemRights;
            }
        }

        return allowManagedRights;
    }

    private static FileSystemRights BuildEntryRights(AllowAclEntry entry)
    {
        var rights = FileSystemRights.Read | FileSystemRights.Synchronize;
        if (entry.AllowWrite)
        {
            rights |= FileSystemRights.WriteData | FileSystemRights.AppendData |
                      FileSystemRights.WriteExtendedAttributes | FileSystemRights.WriteAttributes |
                      FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles;
        }

        if (entry.AllowExecute)
            rights |= FileSystemRights.ExecuteFile;

        return rights;
    }
}
