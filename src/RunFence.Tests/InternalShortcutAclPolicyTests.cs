using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Apps.Shortcuts;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public sealed class InternalShortcutAclPolicyTests
{
    private readonly InternalShortcutAclPolicy _policy = new();

    [Fact]
    public void ManagedRulesMatchDesired_NormalizesSynchronizeOnAllowRules()
    {
        var accountSid = WindowsIdentity.GetCurrent().User!.Value;
        var rules = CreateDesiredRules(accountSid, includeSynchronize: true);

        Assert.True(_policy.ManagedRulesMatchDesired(rules, accountSid));
    }

    [Fact]
    public void ManagedRulesMatchDesired_RejectsLegacyUsersRule()
    {
        var accountSid = WindowsIdentity.GetCurrent().User!.Value;
        var rules = CreateDesiredRules(accountSid, includeSynchronize: true);
        rules.Add(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
            AccessControlType.Allow));

        Assert.False(_policy.ManagedRulesMatchDesired(rules, accountSid));
    }

    [Fact]
    public void CreateManagedSids_IncludesLegacyUsersAndEveryoneForCleanup()
    {
        var accountSid = WindowsIdentity.GetCurrent().User!.Value;
        var managedSids = _policy.CreateManagedSids(accountSid);

        Assert.Contains(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), managedSids);
        Assert.Contains(new SecurityIdentifier(WellKnownSidType.WorldSid, null), managedSids);
    }

    private static List<FileSystemAccessRule> CreateDesiredRules(string accountSid, bool includeSynchronize)
    {
        var synchronize = includeSynchronize ? FileSystemRights.Synchronize : 0;
        var rules = new List<FileSystemAccessRule>
        {
            new(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl | synchronize,
                AccessControlType.Allow),
            new(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl | synchronize,
                AccessControlType.Allow),
            new(
                new SecurityIdentifier(accountSid),
                FileSystemRights.ReadAndExecute | synchronize,
                AccessControlType.Allow),
        };

        var currentMockSid = AdminOperationMockAccessHelper.GetCurrentProcessSidWhenUsingMocks();
        if (currentMockSid != null)
            rules.Add(new FileSystemAccessRule(currentMockSid, FileSystemRights.FullControl | synchronize, AccessControlType.Allow));

        return rules;
    }
}
