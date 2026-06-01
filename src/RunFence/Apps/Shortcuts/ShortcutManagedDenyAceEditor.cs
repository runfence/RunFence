using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;

namespace RunFence.Apps.Shortcuts;

public class ShortcutManagedDenyAceEditor(IPathSecurityDescriptorAccessor aclAccessor)
{
    public const FileSystemRights ManagedDenyRights = FileSystemRights.Delete |
                                                      FileSystemRights.Write |
                                                      FileSystemRights.WriteAttributes |
                                                      FileSystemRights.AppendData;

    public bool HasManagedDenyAce(string shortcutPath)
        => FindManagedDenyAce(aclAccessor.GetSecurity(shortcutPath)) != null;

    public void AddManagedDenyAce(string shortcutPath)
    {
        aclAccessor.ModifyAclWithFallback(shortcutPath, security =>
        {
            if (FindManagedDenyAce(security) != null)
                return false;

            security.AddAccessRule(new FileSystemAccessRule(
                GetEveryoneSid(),
                ManagedDenyRights,
                AccessControlType.Deny));
            return true;
        });
    }

    public void RemoveManagedDenyAce(string shortcutPath)
    {
        aclAccessor.ModifyAclWithFallback(shortcutPath, security =>
        {
            var rule = FindManagedDenyAce(security);
            if (rule == null)
                return false;

            security.RemoveAccessRuleSpecific(rule);
            return true;
        });
    }

    internal static FileSystemAccessRule? FindManagedDenyAce(FileSystemSecurity security)
    {
        var everyoneSid = GetEveryoneSid();
        foreach (FileSystemAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Deny ||
                rule.IdentityReference is not SecurityIdentifier sid ||
                !sid.Equals(everyoneSid))
            {
                continue;
            }

            if (NormalizeDenyRights(rule.FileSystemRights) == ManagedDenyRights)
                return rule;
        }

        return null;
    }

    internal static FileSystemRights NormalizeDenyRights(FileSystemRights rights)
        => rights & ~FileSystemRights.Synchronize;

    private static SecurityIdentifier GetEveryoneSid()
        => new(WellKnownSidType.WorldSid, null);
}
