using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;

namespace RunFence.Apps.Shortcuts;

public sealed class InternalShortcutAclEditor(
    IPathSecurityDescriptorAccessor aclAccessor,
    InternalShortcutAclPolicy policy)
{
    public void Protect(string shortcutPath, string accountSid)
    {
        var managedSids = policy.CreateManagedSids(accountSid);

        aclAccessor.ModifyAclWithFallback(shortcutPath, security =>
        {
            var inheritanceBroken = security.AreAccessRulesProtected;
            var existingManaged = new List<FileSystemAccessRule>();
            foreach (FileSystemAccessRule rule in security.GetAccessRules(true, false, typeof(SecurityIdentifier)))
            {
                if (rule.IdentityReference is not SecurityIdentifier sid || !managedSids.Contains(sid))
                    continue;

                existingManaged.Add(rule);
            }

            var managedRulesMatch = policy.ManagedRulesMatchDesired(existingManaged, accountSid);
            var aclChanged = !inheritanceBroken || !managedRulesMatch;
            if (!aclChanged)
                return false;

            if (!inheritanceBroken)
                security.SetAccessRuleProtection(true, false);

            if (!managedRulesMatch)
            {
                foreach (var rule in existingManaged)
                    security.RemoveAccessRuleSpecific(rule);

                foreach (var rule in policy.CreateDesiredRules(accountSid))
                    security.AddAccessRule(rule);
            }

            return true;
        });
    }
}
