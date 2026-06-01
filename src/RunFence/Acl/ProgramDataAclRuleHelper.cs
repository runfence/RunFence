using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl.Traverse;

namespace RunFence.Acl;

internal static class ProgramDataAclRuleHelper
{
    private static readonly SecurityIdentifier CreatorOwnerSid = new(WellKnownSidType.CreatorOwnerSid, null);
    private static readonly SecurityIdentifier OwnerRightsSid = new("S-1-3-4");

    public static IEnumerable<FileSystemAccessRule> GetExplicitRules(FileSystemSecurity security)
        => security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>();

    public static IEnumerable<FileSystemAccessRule> GetAllRules(FileSystemSecurity security)
        => security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>();

    public static bool IsOwnerRelativeRule(FileSystemAccessRule rule)
        => rule.IdentityReference is SecurityIdentifier sid &&
           (sid.Equals(CreatorOwnerSid) || sid.Equals(OwnerRightsSid));

    public static bool IsExactTraverseAce(FileSystemAccessRule rule)
        => rule.AccessControlType == AccessControlType.Allow &&
           rule.FileSystemRights == TraverseRightsHelper.TraverseRights &&
           rule.InheritanceFlags == InheritanceFlags.None &&
           rule.PropagationFlags == PropagationFlags.None;

    public static void AddRuleDeduped(FileSystemSecurity security, FileSystemAccessRule rule)
    {
        if (GetExplicitRules(security).Any(existing => AreEquivalentRules(existing, rule)))
        {
            return;
        }

        security.AddAccessRule(rule);
    }

    public static string GetSecuritySignature(FileSystemSecurity security)
    {
        var rules = GetExplicitRules(security)
            .Select(rule =>
            {
                var sid = (SecurityIdentifier)rule.IdentityReference;
                return $"{sid.Value}|{rule.AccessControlType}|{(int)rule.FileSystemRights}|{(int)rule.InheritanceFlags}|{(int)rule.PropagationFlags}";
            })
            .OrderBy(x => x, StringComparer.Ordinal);
        return $"{security.AreAccessRulesProtected}:{string.Join(";", rules)}";
    }

    private static bool AreEquivalentRules(FileSystemAccessRule left, FileSystemAccessRule right)
        => left.AccessControlType == right.AccessControlType &&
           left.IdentityReference.Equals(right.IdentityReference) &&
           left.FileSystemRights == right.FileSystemRights &&
           left.InheritanceFlags == right.InheritanceFlags &&
           left.PropagationFlags == right.PropagationFlags;
}
