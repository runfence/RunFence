using System.Security.AccessControl;
using System.Security.Principal;

namespace RunFence.Acl;

internal static class ProgramDataSecurityChangeFormatter
{
    public static string DescribeSecurityState(FileSystemSecurity security)
    {
        var parts = new List<string>();
        var owner = GetOwner(security);
        if (owner != null)
        {
            parts.Add($"owner {owner}");
        }

        parts.Add($"protected DACL {security.AreAccessRulesProtected}");

        var rules = GetRuleSignatures(security);
        parts.Add(rules.Count == 0
            ? "rules []"
            : $"rules [{string.Join(", ", rules)}]");
        return string.Join("; ", parts);
    }

    public static string DescribeAccessChange(FileSystemSecurity before, FileSystemSecurity after)
    {
        var parts = new List<string>();
        if (before.AreAccessRulesProtected != after.AreAccessRulesProtected)
        {
            parts.Add($"protected DACL {before.AreAccessRulesProtected}->{after.AreAccessRulesProtected}");
        }

        if (before.AreAccessRulesCanonical != after.AreAccessRulesCanonical)
        {
            parts.Add($"canonical DACL {before.AreAccessRulesCanonical}->{after.AreAccessRulesCanonical}");
        }

        var beforeRules = GetRuleSignatures(before);
        var afterRules = GetRuleSignatures(after);
        var removedRules = beforeRules.Except(afterRules, StringComparer.Ordinal).ToList();
        var addedRules = afterRules.Except(beforeRules, StringComparer.Ordinal).ToList();

        if (removedRules.Count > 0)
        {
            parts.Add($"removed [{string.Join(", ", removedRules)}]");
        }

        if (addedRules.Count > 0)
        {
            parts.Add($"added [{string.Join(", ", addedRules)}]");
        }

        return parts.Count == 0 ? "no ACL delta" : string.Join("; ", parts);
    }

    public static string DescribeFullSecurityChange(FileSystemSecurity before, FileSystemSecurity after)
    {
        var parts = new List<string>();
        var beforeOwner = GetOwner(before);
        var afterOwner = GetOwner(after);
        if (!string.Equals(beforeOwner, afterOwner, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"owner {beforeOwner}->{afterOwner}");
        }

        var accessDelta = DescribeAccessChange(before, after);
        if (!string.Equals(accessDelta, "no ACL delta", StringComparison.Ordinal))
        {
            parts.Add(accessDelta);
        }

        return parts.Count == 0 ? "no security delta" : string.Join("; ", parts);
    }

    private static string? GetOwner(FileSystemSecurity security)
        => (SecurityIdentifier?)security.GetOwner(typeof(SecurityIdentifier)) is { } owner ? owner.Value : null;

    private static List<string> GetRuleSignatures(FileSystemSecurity security)
        => ProgramDataAclRuleHelper.GetExplicitRules(security)
            .Select(rule =>
            {
                var sid = (SecurityIdentifier)rule.IdentityReference;
                return $"{sid.Value}:{rule.AccessControlType}:{(int)rule.FileSystemRights}:{(int)rule.InheritanceFlags}:{(int)rule.PropagationFlags}";
            })
            .OrderBy(signature => signature, StringComparer.Ordinal)
            .ToList();
}
