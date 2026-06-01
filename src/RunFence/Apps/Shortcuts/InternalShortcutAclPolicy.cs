using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;

namespace RunFence.Apps.Shortcuts;

public sealed class InternalShortcutAclPolicy
{
    public IReadOnlyCollection<FileSystemAccessRule> CreateDesiredRules(string accountSid)
    {
        var rules = new List<FileSystemAccessRule>
        {
            new(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), FileSystemRights.FullControl, AccessControlType.Allow),
            new(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), FileSystemRights.FullControl, AccessControlType.Allow),
            new(new SecurityIdentifier(accountSid), FileSystemRights.ReadAndExecute, AccessControlType.Allow),
        };

        var currentMockSid = AdminOperationMockAccessHelper.GetCurrentProcessSidWhenUsingMocks();
        if (currentMockSid != null)
            rules.Add(new FileSystemAccessRule(currentMockSid, FileSystemRights.FullControl, AccessControlType.Allow));

        return rules;
    }

    public IReadOnlySet<SecurityIdentifier> CreateManagedSids(string accountSid)
    {
        var managedSids = new HashSet<SecurityIdentifier>
        {
            new(WellKnownSidType.LocalSystemSid, null),
            new(WellKnownSidType.BuiltinAdministratorsSid, null),
            new(accountSid),
            new(WellKnownSidType.BuiltinUsersSid, null),
            new(WellKnownSidType.WorldSid, null),
        };

        var currentMockSid = AdminOperationMockAccessHelper.GetCurrentProcessSidWhenUsingMocks();
        if (currentMockSid != null)
            managedSids.Add(currentMockSid);

        return managedSids;
    }

    public bool ManagedRulesMatchDesired(IEnumerable<FileSystemAccessRule> managedRules, string accountSid)
    {
        var desired = BuildRuleSet(CreateDesiredRules(accountSid));
        var existing = BuildRuleSet(managedRules);
        return desired.SetEquals(existing);
    }

    private HashSet<RuleKey> BuildRuleSet(IEnumerable<FileSystemAccessRule> rules)
    {
        var set = new HashSet<RuleKey>();
        foreach (var rule in rules)
        {
            if (rule.IdentityReference is SecurityIdentifier sid)
                set.Add(new RuleKey(sid.Value, NormalizeRights(rule), rule.AccessControlType));
        }

        return set;
    }

    private static FileSystemRights NormalizeRights(FileSystemAccessRule rule)
        => rule.AccessControlType == AccessControlType.Allow
            ? rule.FileSystemRights & ~FileSystemRights.Synchronize
            : rule.FileSystemRights;

    private readonly record struct RuleKey(string Sid, FileSystemRights Rights, AccessControlType Type);
}
