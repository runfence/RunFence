using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Acl;
using Xunit;

namespace RunFence.Tests;

public class ProgramDataManagedObjectRepairerTests
{
    [Fact]
    public void EnsureManagedFileSecurity_WhenOnlyOwnerIsUntrusted_ReportsOwnerChangedWithoutDaclChange()
    {
        using var scope = new ProgramDataSecurityTestScope();
        var filePath = scope.CreateFile(@"PrefTransLogs\state.json");
        var attackerSid = new SecurityIdentifier("S-1-5-21-9999-9999-9999-1001");
        var security = new ProgramDataDirectoryAclBuilder(new ProgramDataAclProfilePolicy()).BuildFileSecurity(
            ProgramDataFileAclProfile.TrustedOnly);
        security.SetOwner(attackerSid);
        scope.State.SetFileSecurity(filePath, security);

        var result = scope.ManagedObjectRepairer.EnsureManagedFileSecurity(filePath, ProgramDataFileAclProfile.TrustedOnly);

        Assert.True(result.OwnerChanged);
        Assert.True(result.RemovedUntrustedWriteOrOwnerAccess);
        Assert.False(result.DaclChanged);
        Assert.Equal(scope.CurrentUserSid.Value, ProgramDataSecurityTestScope.GetOwnerSid(scope.State.GetFileSecurity(filePath)).Value);
    }

    [Fact]
    public void EnsureManagedFileSecurity_WhenOnlyDaclIsUntrusted_ReportsUntrustedAccessWithoutOwnerChange()
    {
        using var scope = new ProgramDataSecurityTestScope();
        var filePath = scope.CreateFile(@"PrefTransLogs\state.json");
        var security = new FileSecurity();
        security.SetOwner(scope.CurrentUserSid);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Modify,
            AccessControlType.Allow));
        scope.State.SetFileSecurity(filePath, security);

        var result = scope.ManagedObjectRepairer.EnsureManagedFileSecurity(filePath, ProgramDataFileAclProfile.TrustedOnly);

        Assert.False(result.OwnerChanged);
        Assert.True(result.RemovedUntrustedWriteOrOwnerAccess);
        Assert.True(result.DaclChanged);
        Assert.DoesNotContain(
            ProgramDataSecurityTestScope.GetExplicitRules(scope.State.GetFileSecurity(filePath)),
            rule => ProgramDataSecurityTestScope.IsRuleForSid(rule, WellKnownSidType.WorldSid) &&
                    (rule.FileSystemRights & FileSystemRights.Modify) != 0);
    }
}
