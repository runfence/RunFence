using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Moq;
using RunFence.Acl;
using RunFence.Acl.Traverse;
using RunFence.Core;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class ProgramDataDirectorySecurityServiceTests
{
    [Fact]
    public void ProgramDataDirectoryAclBuilder_PreservesOwnerRelativeAndTraverseAces_AndCollapsesDuplicates()
    {
        var builder = new ProgramDataDirectoryAclBuilder(new ProgramDataAclProfilePolicy());
        var existingSecurity = new DirectorySecurity();
        var traverseSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        existingSecurity.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.InheritOnly,
            AccessControlType.Allow));
        existingSecurity.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier("S-1-3-4"),
            FileSystemRights.ReadData,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        existingSecurity.AddAccessRule(new FileSystemAccessRule(
            traverseSid,
            TraverseRightsHelper.TraverseRights,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        existingSecurity.AddAccessRule(new FileSystemAccessRule(
            traverseSid,
            TraverseRightsHelper.TraverseRights,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        existingSecurity.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));

        var rebuilt = builder.BuildDirectorySecurity(
            ProgramDataDirectoryAclProfile.TrustedOnly,
            existingSecurity,
            additionalTraverseSid: traverseSid.Value);

        var rules = GetExplicitRules(rebuilt).ToList();
        Assert.Contains(rules, rule => IsRuleForSid(rule, WellKnownSidType.CreatorOwnerSid));
        Assert.Contains(rules, rule => IsRuleForSid(rule, "S-1-3-4"));
        Assert.Single(rules, rule => IsExactTraverseRule(rule, traverseSid.Value));
        Assert.DoesNotContain(rules, rule =>
            IsRuleForSid(rule, WellKnownSidType.WorldSid) &&
            rule.FileSystemRights == FileSystemRights.Modify);
    }

    [Fact]
    public void ProgramDataDirectoryAclBuilder_PublicIconRead_AddsOnlyWorldReadBeyondTrustedPrincipals()
    {
        var builder = new ProgramDataDirectoryAclBuilder(new ProgramDataAclProfilePolicy());

        var security = builder.BuildFileSecurity(ProgramDataFileAclProfile.PublicIconRead);

        Assert.Contains(GetExplicitRules(security), rule =>
            IsRuleForSid(rule, WellKnownSidType.WorldSid) &&
            rule.AccessControlType == AccessControlType.Allow &&
            (rule.FileSystemRights & FileSystemRights.ReadData) != 0 &&
            rule.InheritanceFlags == InheritanceFlags.None &&
            rule.PropagationFlags == PropagationFlags.None);
        Assert.DoesNotContain(GetExplicitRules(security), rule =>
            IsRuleForSid(rule, WellKnownSidType.WorldSid) &&
            (rule.FileSystemRights & (
                FileSystemRights.WriteData |
                FileSystemRights.AppendData |
                FileSystemRights.Delete |
                FileSystemRights.TakeOwnership)) != 0);
    }

    [Fact]
    public void ProgramDataDirectoryAclBuilder_SharedExecutableReadExecute_AddsOnlyBuiltinUsersReadExecuteBeyondTrustedPrincipals()
    {
        var builder = new ProgramDataDirectoryAclBuilder(new ProgramDataAclProfilePolicy());

        var directorySecurity = builder.BuildDirectorySecurity(
            ProgramDataDirectoryAclProfile.SharedExecutableReadExecute,
            existingSecurity: null);
        var fileSecurity = builder.BuildFileSecurity(ProgramDataFileAclProfile.SharedExecutableReadExecute);

        Assert.Contains(GetExplicitRules(directorySecurity), rule =>
            IsRuleForSid(rule, WellKnownSidType.BuiltinUsersSid) &&
            rule.AccessControlType == AccessControlType.Allow &&
            (rule.FileSystemRights & FileSystemRights.ReadAndExecute) == FileSystemRights.ReadAndExecute &&
            rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit));
        Assert.Contains(GetExplicitRules(fileSecurity), rule =>
            IsRuleForSid(rule, WellKnownSidType.BuiltinUsersSid) &&
            rule.AccessControlType == AccessControlType.Allow &&
            (rule.FileSystemRights & FileSystemRights.ReadAndExecute) == FileSystemRights.ReadAndExecute &&
            rule.InheritanceFlags == InheritanceFlags.None);
        Assert.DoesNotContain(GetExplicitRules(fileSecurity), rule =>
            IsRuleForSid(rule, WellKnownSidType.BuiltinUsersSid) &&
            (rule.FileSystemRights & (
                FileSystemRights.WriteData |
                FileSystemRights.AppendData |
                FileSystemRights.Delete |
                FileSystemRights.TakeOwnership)) != 0);
    }

    [Fact]
    public void EnsureDirectoryUnderRoot_SharedExecutableReadExecute_AcceptsReadExecuteWithSynchronize()
    {
        using var scope = new ProgramDataTestScope();
        var childPath = scope.CreateDirectory("WindowsTerminal");
        var security = new DirectorySecurity();
        security.SetOwner(scope.CurrentUserSid);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        scope.State.SetDirectorySecurity(childPath, security);

        scope.Service.EnsureDirectoryUnderRoot(childPath, ProgramDataDirectoryAclProfile.SharedExecutableReadExecute);

        var verified = scope.State.GetDirectorySecurity(childPath);
        Assert.Contains(GetExplicitRules(verified), rule =>
            IsRuleForSid(rule, WellKnownSidType.BuiltinUsersSid) &&
            rule.AccessControlType == AccessControlType.Allow &&
            (rule.FileSystemRights & FileSystemRights.ReadAndExecute) == FileSystemRights.ReadAndExecute &&
            (rule.FileSystemRights & FileSystemRights.Synchronize) == FileSystemRights.Synchronize &&
            rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit));
    }

    [Fact]
    public void EnsureDirectoryUnderRoot_RepairsAttackerOwnerAndRemovesUntrustedWriteRules()
    {
        using var scope = new ProgramDataTestScope();
        var childPath = scope.CreateDirectory("PackageInstallScripts");
        var attackerSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);

        var security = new DirectorySecurity();
        security.SetOwner(attackerSid);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.ReadData | FileSystemRights.WriteData | FileSystemRights.Delete,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            attackerSid,
            FileSystemRights.CreateDirectories,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        scope.State.SetDirectorySecurity(childPath, security);

        scope.Service.EnsureDirectoryUnderRoot(childPath, ProgramDataDirectoryAclProfile.TrustedOnly);

        var repaired = scope.State.GetDirectorySecurity(childPath);
        Assert.Equal(scope.CurrentUserSid.Value, GetOwnerSid(repaired).Value);
        Assert.DoesNotContain(GetExplicitRules(repaired), rule =>
            rule.AccessControlType == AccessControlType.Allow &&
            (((SecurityIdentifier)rule.IdentityReference).IsWellKnown(WellKnownSidType.WorldSid) ||
             ((SecurityIdentifier)rule.IdentityReference).Equals(attackerSid)) &&
            (rule.FileSystemRights & (
                FileSystemRights.ReadData |
                FileSystemRights.WriteData |
                FileSystemRights.Delete |
                FileSystemRights.CreateDirectories)) != 0);
    }

    [Fact]
    public void EnsureDirectoryUnderRoot_ReplacesNonCanonicalDaclWithoutMutatingItInPlace()
    {
        using var scope = new ProgramDataTestScope();
        var childPath = scope.CreateDirectory("PrefTransLogs");
        scope.State.SetDirectorySecurity(childPath, CreateNonCanonicalDirectorySecurity(scope.CurrentUserSid));

        scope.Service.EnsureDirectoryUnderRoot(childPath, ProgramDataDirectoryAclProfile.TrustedOnly);

        var repaired = scope.State.GetDirectorySecurity(childPath);
        Assert.True(repaired.AreAccessRulesProtected);
        Assert.Equal(scope.CurrentUserSid.Value, GetOwnerSid(repaired).Value);
        Assert.DoesNotContain(GetExplicitRules(repaired), rule =>
            IsRuleForSid(rule, WellKnownSidType.BuiltinUsersSid) &&
            (rule.FileSystemRights & FileSystemRights.WriteData) != 0);
        Assert.Contains(GetExplicitRules(repaired), rule =>
            IsRuleForSid(rule, WellKnownSidType.LocalSystemSid) &&
            rule.FileSystemRights == FileSystemRights.FullControl);
    }

    [Fact]
    public void EnsureDirectoryUnderRoot_WhenAclAlreadyMatches_DoesNotLogAclUpdate()
    {
        var log = new Mock<ILoggingService>();
        using var scope = new ProgramDataTestScope(log);
        var childPath = scope.CreateDirectory("WindowsTerminal");
        var security = new ProgramDataDirectoryAclBuilder(new ProgramDataAclProfilePolicy()).BuildDirectorySecurity(
            ProgramDataDirectoryAclProfile.SharedExecutableReadExecute,
            existingSecurity: null);
        security.SetOwner(scope.CurrentUserSid);
        scope.State.SetDirectorySecurity(childPath, security);

        scope.Service.EnsureDirectoryUnderRoot(childPath, ProgramDataDirectoryAclProfile.SharedExecutableReadExecute);

        log.Verify(
            logger => logger.Info(It.Is<string>(message =>
                message.StartsWith($"ProgramData security updated directory ACL on '{childPath}'", StringComparison.Ordinal))),
            Times.Never);
    }

    [Fact]
    public void EnsureDirectoryTreeInheritsFromRoot_RemovesExplicitChildAcls_AndIsNoOpAfterRepair()
    {
        var log = new Mock<ILoggingService>();
        using var scope = new ProgramDataTestScope(log);
        var rootPath = scope.CreateDirectory("WindowsTerminal");
        var childDirectoryPath = scope.CreateDirectory(@"WindowsTerminal\path");
        var childFilePath = scope.CreateFile(@"WindowsTerminal\path\wt.cmd");
        var attackerSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        scope.State.SetDirectorySecurity(childDirectoryPath, CreateUntrustedProtectedDirectorySecurity(attackerSid));
        scope.State.SetFileSecurity(childFilePath, CreateUntrustedFileSecurity(attackerSid));

        scope.Service.EnsureDirectoryTreeInheritsFromRoot(
            rootPath,
            ProgramDataDirectoryAclProfile.SharedExecutableReadExecute);

        var repairedDirectory = scope.State.GetDirectorySecurity(childDirectoryPath);
        var repairedFile = scope.State.GetFileSecurity(childFilePath);
        Assert.False(repairedDirectory.AreAccessRulesProtected);
        Assert.False(repairedFile.AreAccessRulesProtected);
        Assert.Equal(scope.CurrentUserSid.Value, GetOwnerSid(repairedDirectory).Value);
        Assert.Equal(scope.CurrentUserSid.Value, GetOwnerSid(repairedFile).Value);
        Assert.Empty(GetExplicitRules(repairedDirectory));
        Assert.Empty(GetExplicitRules(repairedFile));

        scope.Service.EnsureDirectoryTreeInheritsFromRoot(
            rootPath,
            ProgramDataDirectoryAclProfile.SharedExecutableReadExecute);

        log.Verify(
            logger => logger.Info(It.Is<string>(message =>
                message.StartsWith($"ProgramData security propagated inherited ACLs under '{rootPath}'", StringComparison.Ordinal))),
            Times.Once);
    }

    [Fact]
    public void EnsureDirectoryTreeInheritsFromRoot_RejectsReparseChildBeforeDescending()
    {
        using var scope = new ProgramDataTestScope();
        var rootPath = scope.CreateDirectory("WindowsTerminal");
        var childDirectoryPath = scope.CreateDirectory(@"WindowsTerminal\junction");
        scope.PathGuard.RejectAsReparse(childDirectoryPath);

        var exception = Assert.Throws<InvalidOperationException>(() => scope.Service.EnsureDirectoryTreeInheritsFromRoot(
            rootPath,
            ProgramDataDirectoryAclProfile.SharedExecutableReadExecute));

        Assert.Contains("must not be a reparse point", exception.Message);
    }

    [Fact]
    public void EnsureManagedFileSecurity_WhenAclAlreadyMatches_DoesNotLogAclUpdate()
    {
        var log = new Mock<ILoggingService>();
        using var scope = new ProgramDataTestScope(log);
        var filePath = scope.CreateFile(@"WindowsTerminal\WindowsTerminal.exe");
        var security = new ProgramDataDirectoryAclBuilder(new ProgramDataAclProfilePolicy()).BuildFileSecurity(
            ProgramDataFileAclProfile.SharedExecutableReadExecute);
        security.SetOwner(scope.CurrentUserSid);
        scope.State.SetFileSecurity(filePath, security);

        var result = scope.Service.EnsureManagedFileSecurity(
            filePath,
            ProgramDataFileAclProfile.SharedExecutableReadExecute);

        Assert.False(result.DaclChanged);
        log.Verify(
            logger => logger.Info(It.Is<string>(message =>
                message.StartsWith($"ProgramData security updated file ACL on '{filePath}'", StringComparison.Ordinal))),
            Times.Never);
    }

    [Theory]
    [MemberData(nameof(PreservedManagedFileOwnerSids))]
    public void EnsureManagedFileOwner_PreservesAllowedOwner_LeavesDaclUntouched_AndUsesOwnerRepair(string ownerSidValue)
    {
        using var scope = new ProgramDataTestScope();
        var filePath = scope.CreateFile(@"PrefTransLogs\state.json");
        var security = new FileSecurity();
        security.SetOwner(new SecurityIdentifier(ownerSidValue));
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadData,
            AccessControlType.Allow));
        scope.State.SetFileSecurity(filePath, security);
        var before = GetSecuritySignature(scope.State.GetFileSecurity(filePath));

        scope.Service.EnsureManagedFileOwner(filePath);

        var afterSecurity = scope.State.GetFileSecurity(filePath);
        Assert.Equal(ownerSidValue, GetOwnerSid(afterSecurity).Value);
        Assert.Equal(before, GetSecuritySignature(afterSecurity));
        Assert.All(scope.PathGuard.OpenCalls, call => Assert.Equal(ProgramDataManagedObjectAccess.OwnerRepair, call.Access));
        Assert.Equal(0, scope.Accessor.HandleModifyCallCount);
    }

    [Fact]
    public void EnsureManagedDirectoryOwner_ReplacesPreviousOwner_LeavesDaclUntouched_AndUsesOwnerRepair()
    {
        using var scope = new ProgramDataTestScope();
        var directoryPath = scope.CreateDirectory(@"PrefTransLogs");
        var previousOwner = new SecurityIdentifier("S-1-5-21-9999-9999-9999-1001");
        var security = new DirectorySecurity();
        security.SetOwner(previousOwner);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadData,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        scope.State.SetDirectorySecurity(directoryPath, security);
        var before = GetDaclSignature(scope.State.GetDirectorySecurity(directoryPath));

        scope.Service.EnsureManagedDirectoryOwner(directoryPath);

        var afterSecurity = scope.State.GetDirectorySecurity(directoryPath);
        Assert.Equal(scope.CurrentUserSid.Value, GetOwnerSid(afterSecurity).Value);
        Assert.Equal(before, GetDaclSignature(afterSecurity));
        Assert.All(scope.PathGuard.OpenCalls, call => Assert.Equal(ProgramDataManagedObjectAccess.OwnerRepair, call.Access));
        Assert.Equal(0, scope.Accessor.HandleModifyCallCount);
    }

    [Fact]
    public void EnsureManagedDirectoryOwner_ForAcTree_PreservesOnlyExpectedInteractiveAndPackageOwners()
    {
        using var scope = new ProgramDataTestScope();
        var acDirectory = scope.CreateDirectory(@"AC\ProfileA");
        var interactiveSid = new SecurityIdentifier("S-1-5-21-1111-2222-3333-1002");
        var expectedPackageSid = new SecurityIdentifier("S-1-15-2-123456789-123456789-123456789-123456789-1234");
        var differentPackageSid = new SecurityIdentifier("S-1-15-2-987654321-987654321-987654321-987654321-4321");

        scope.State.SetDirectorySecurity(acDirectory, CreateOwnedDirectorySecurity(interactiveSid));
        scope.Service.EnsureManagedDirectoryOwner(acDirectory, [interactiveSid.Value, expectedPackageSid.Value]);
        Assert.Equal(interactiveSid.Value, GetOwnerSid(scope.State.GetDirectorySecurity(acDirectory)).Value);

        scope.State.SetDirectorySecurity(acDirectory, CreateOwnedDirectorySecurity(expectedPackageSid));
        scope.Service.EnsureManagedDirectoryOwner(acDirectory, [interactiveSid.Value, expectedPackageSid.Value]);
        Assert.Equal(expectedPackageSid.Value, GetOwnerSid(scope.State.GetDirectorySecurity(acDirectory)).Value);

        scope.State.SetDirectorySecurity(acDirectory, CreateOwnedDirectorySecurity(differentPackageSid));
        scope.Service.EnsureManagedDirectoryOwner(acDirectory, [interactiveSid.Value, expectedPackageSid.Value]);
        Assert.Equal(scope.CurrentUserSid.Value, GetOwnerSid(scope.State.GetDirectorySecurity(acDirectory)).Value);
    }

    [Fact]
    public void EnsureManagedFileOwner_ForAcTree_PreservesOnlyExpectedInteractiveAndPackageOwners()
    {
        using var scope = new ProgramDataTestScope();
        var acFile = scope.CreateFile(@"AC\ProfileA\settings.json");
        var interactiveSid = new SecurityIdentifier("S-1-5-21-1111-2222-3333-1002");
        var expectedPackageSid = new SecurityIdentifier("S-1-15-2-123456789-123456789-123456789-123456789-1234");
        var differentPackageSid = new SecurityIdentifier("S-1-15-2-987654321-987654321-987654321-987654321-4321");

        scope.State.SetFileSecurity(acFile, CreateOwnedFileSecurity(interactiveSid));
        scope.Service.EnsureManagedFileOwner(acFile, [interactiveSid.Value, expectedPackageSid.Value]);
        Assert.Equal(interactiveSid.Value, GetOwnerSid(scope.State.GetFileSecurity(acFile)).Value);

        scope.State.SetFileSecurity(acFile, CreateOwnedFileSecurity(expectedPackageSid));
        scope.Service.EnsureManagedFileOwner(acFile, [interactiveSid.Value, expectedPackageSid.Value]);
        Assert.Equal(expectedPackageSid.Value, GetOwnerSid(scope.State.GetFileSecurity(acFile)).Value);

        scope.State.SetFileSecurity(acFile, CreateOwnedFileSecurity(differentPackageSid));
        scope.Service.EnsureManagedFileOwner(acFile, [interactiveSid.Value, expectedPackageSid.Value]);
        Assert.Equal(scope.CurrentUserSid.Value, GetOwnerSid(scope.State.GetFileSecurity(acFile)).Value);
    }

    [Fact]
    public void EnsureManagedFileSecurity_ReplacesAttackerControlledFileDacl_AndReturnsTrustRepairFlags()
    {
        using var scope = new ProgramDataTestScope();
        var filePath = scope.CreateFile(@"PrefTransLogs\state.json");
        var attackerSid = new SecurityIdentifier("S-1-5-21-9999-9999-9999-1001");
        var security = new FileSecurity();
        security.SetOwner(attackerSid);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Modify,
            AccessControlType.Allow));
        scope.State.SetFileSecurity(filePath, security);

        var result = scope.Service.EnsureManagedFileSecurity(filePath, ProgramDataFileAclProfile.TrustedOnly);

        Assert.True(result.OwnerChanged);
        Assert.True(result.RemovedUntrustedWriteOrOwnerAccess);
        Assert.True(result.DaclChanged);
        var repaired = scope.State.GetFileSecurity(filePath);
        Assert.Equal(scope.CurrentUserSid.Value, GetOwnerSid(repaired).Value);
        Assert.DoesNotContain(GetExplicitRules(repaired), rule =>
            IsRuleForSid(rule, WellKnownSidType.WorldSid) &&
            (rule.FileSystemRights & FileSystemRights.Modify) != 0);
    }

    [Fact]
    public void EnsureRoot_ReplacesPreviousOwner_LeavesInheritedDaclUntouched_AndUsesOwnerRepair()
    {
        using var scope = new ProgramDataTestScope();
        var previousOwner = new SecurityIdentifier("S-1-5-21-9999-9999-9999-1001");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
        security.SetOwner(previousOwner);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadData,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        scope.State.SetDirectorySecurity(scope.RootPath, security);
        var before = GetDaclSignature(scope.State.GetDirectorySecurity(scope.RootPath));

        scope.Service.EnsureRoot();

        var afterSecurity = scope.State.GetDirectorySecurity(scope.RootPath);
        Assert.Equal(scope.CurrentUserSid.Value, GetOwnerSid(afterSecurity).Value);
        Assert.Equal(before, GetDaclSignature(afterSecurity));
        Assert.Equal(0, scope.Accessor.HandleModifyCallCount);
        Assert.All(scope.PathGuard.OpenCalls, call =>
        {
            Assert.Equal(scope.RootPath, call.Path);
            Assert.Equal(ProgramDataManagedObjectAccess.OwnerRepair, call.Access);
        });
    }

    [Fact]
    public void EnsureSubdirectory_UnknownTopLevelDirectory_IsRejected()
    {
        using var scope = new ProgramDataTestScope();

        var exception = Assert.Throws<InvalidOperationException>(() => scope.Service.EnsureDirectoryUnderRoot(
            Path.Combine(scope.RootPath, "ShortcutProtectionState", "child"),
            ProgramDataDirectoryAclProfile.PublicReadTrustedWrite));

        Assert.Contains("did not resolve to a directory profile", exception.Message);
    }

    [Fact]
    public void EnsureSubdirectory_DynamicLeafCanUseDifferentProfileThanKnownRoot()
    {
        using var scope = new ProgramDataTestScope();

        var created = Path.Combine(
            scope.RootPath,
            ProgramDataPolicies.WindowsTerminalDeploymentWork.RelativePath,
            "operation",
            "staging");
        scope.Service.EnsureDirectoryUnderRoot(
            created,
            ProgramDataDirectoryAclProfile.SharedExecutableReadExecute);

        var rootPath = Path.Combine(scope.RootPath, ProgramDataPolicies.WindowsTerminalDeploymentWork.RelativePath);
        var operationPath = Path.GetDirectoryName(created)!;
        Assert.DoesNotContain(GetExplicitRules(scope.State.GetDirectorySecurity(rootPath)), IsBuiltinUsersReadExecuteRule);
        Assert.True(Directory.Exists(operationPath));
        Assert.DoesNotContain(scope.NativeFileSystem.CreatedDirectories, path => string.Equals(path, operationPath, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(GetExplicitRules(scope.State.GetDirectorySecurity(created)), IsBuiltinUsersReadExecuteRule);
    }

    [Fact]
    public void EnsureTraverseOnlyAccess_PreservesExactTraverseAndCollapsesDuplicates()
    {
        using var scope = new ProgramDataTestScope();
        var directoryPath = scope.CreateDirectory(ProgramDataPolicies.Temp.RelativePath);
        var traverseSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var security = new DirectorySecurity();
        security.SetOwner(scope.CurrentUserSid);
        security.AddAccessRule(new FileSystemAccessRule(
            traverseSid,
            TraverseRightsHelper.TraverseRights,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        security.AddAccessRule(new FileSystemAccessRule(
            traverseSid,
            TraverseRightsHelper.TraverseRights,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        scope.State.SetDirectorySecurity(directoryPath, security);

        scope.Service.EnsureTraverseOnlyAccess(
            directoryPath,
            traverseSid.Value,
            ProgramDataDirectoryAclProfile.TrustedOnly);

        Assert.Single(
            GetExplicitRules(scope.State.GetDirectorySecurity(directoryPath)),
            rule => IsExactTraverseRule(rule, traverseSid.Value));
        Assert.True(scope.Accessor.HandleGetSecurityCallCount > 0);
    }

    [Fact]
    public void EnsureTraverseOnlyAccess_OnRoot_LeavesInheritedDaclUntouched()
    {
        using var scope = new ProgramDataTestScope();
        var traverseSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
        security.SetOwner(scope.CurrentUserSid);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadData,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        scope.State.SetDirectorySecurity(scope.RootPath, security);
        var before = GetDaclSignature(scope.State.GetDirectorySecurity(scope.RootPath));

        scope.Service.EnsureTraverseOnlyAccess(
            scope.RootPath,
            traverseSid.Value,
            ProgramDataDirectoryAclProfile.TrustedOnly);

        var afterSecurity = scope.State.GetDirectorySecurity(scope.RootPath);
        Assert.Equal(before, GetDaclSignature(afterSecurity));
        Assert.Equal(0, scope.Accessor.HandleModifyCallCount);
        Assert.All(scope.PathGuard.OpenCalls, call =>
        {
            Assert.Equal(scope.RootPath, call.Path);
            Assert.Equal(ProgramDataManagedObjectAccess.OwnerRepair, call.Access);
        });
    }

    [Fact]
    public void EnsureRootAndEnsureDirectoryUnderRoot_RejectManagedReparsePoints()
    {
        using var rootScope = new ProgramDataTestScope();
        rootScope.PathGuard.RejectAsReparse(rootScope.RootPath);
        Assert.Throws<InvalidOperationException>(() => rootScope.Service.EnsureRoot());

        using var childScope = new ProgramDataTestScope();
        var childPath = childScope.CreateDirectory("icons");
        childScope.PathGuard.RejectAsReparse(childPath);
        Assert.Throws<InvalidOperationException>(() =>
            childScope.Service.EnsureDirectoryUnderRoot(childPath, ProgramDataDirectoryAclProfile.PublicReadTrustedWrite));
    }

    [Fact]
    public void CreateOrReplaceManagedFile_RequiresExistingParentDirectory()
    {
        using var scope = new ProgramDataTestScope();
        var iconsPath = scope.CreateDirectory("icons");
        scope.Service.EnsureKnownDirectory(ProgramDataPolicies.Icons);
        var beforeIcons = GetSecuritySignature(scope.State.GetDirectorySecurity(iconsPath));
        var targetPath = Path.Combine(scope.RootPath, "icons", "nested", "badge.ico");

        var exception = Assert.Throws<InvalidOperationException>(() => scope.Service.CreateOrReplaceManagedFile(
            targetPath,
            ProgramDataFileAclProfile.PublicIconRead));

        Assert.Contains("must already exist", exception.Message);
        Assert.Equal(beforeIcons, GetSecuritySignature(scope.State.GetDirectorySecurity(iconsPath)));
        Assert.Contains(GetExplicitRules(scope.State.GetDirectorySecurity(iconsPath)), rule =>
            ((SecurityIdentifier)rule.IdentityReference).IsWellKnown(WellKnownSidType.WorldSid) &&
            rule.AccessControlType == AccessControlType.Allow &&
            rule.FileSystemRights.HasFlag(FileSystemRights.ReadAndExecute) &&
            rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit));
    }

    [Fact]
    public void CreateOrReplaceManagedFile_RejectsReparsePointOnExistingParentDirectory()
    {
        using var scope = new ProgramDataTestScope();
        var blockedPath = scope.CreateDirectory(@"icons\blocked");
        scope.PathGuard.RejectAsReparse(blockedPath);

        Assert.Throws<InvalidOperationException>(() => scope.Service.CreateOrReplaceManagedFile(
            Path.Combine(scope.RootPath, "icons", "blocked", "badge.ico"),
            ProgramDataFileAclProfile.PublicIconRead));
    }

    [Fact]
    public void CreateOrReplaceManagedFile_RejectsExistingFinalTargetReparsePoint()
    {
        using var scope = new ProgramDataTestScope();
        var iconsPath = scope.CreateDirectory(ProgramDataPolicies.Icons.RelativePath);
        var finalTargetPath = Path.Combine(iconsPath, "badge.ico");
        var junctionTargetPath = scope.CreateDirectory("junction-target");
        JunctionHelper.CreateJunction(finalTargetPath, junctionTargetPath);

        var exception = Assert.Throws<InvalidOperationException>(() => scope.Service.CreateOrReplaceManagedFile(
            finalTargetPath,
            ProgramDataFileAclProfile.PublicIconRead));

        Assert.Contains("must not be a reparse point", exception.Message);
    }

    [Fact]
    public void CreateOrReplaceManagedFile_ExistingFile_RepairsSecurityAndOverwritesContent()
    {
        using var scope = new ProgramDataSecurityTestScope();
        var subdirectory = Path.Combine(ProgramDataPolicies.Icons.RelativePath, "ManagedIcons");
        scope.Service.EnsureDirectoryUnderRoot(
            Path.Combine(scope.RootPath, subdirectory),
            ProgramDataDirectoryAclProfile.TrustedOnly);
        var targetPath = scope.CreateFile(Path.Combine(subdirectory, "icon.ico"));
        var wrongOwner = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var wrongSecurity = new FileSecurity();
        wrongSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        wrongSecurity.SetOwner(wrongOwner);
        wrongSecurity.AddAccessRule(new FileSystemAccessRule(
            wrongOwner,
            FileSystemRights.FullControl,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        scope.State.SetFileSecurity(targetPath, wrongSecurity);
        File.WriteAllBytes(targetPath, new byte[] { 1, 2, 3 });

        var replacementBytes = new byte[] { 9, 8, 7, 6 };
        using (var stream = scope.Service.CreateOrReplaceManagedFile(
                   targetPath,
                   ProgramDataFileAclProfile.PublicIconRead))
        {
            stream.Write(replacementBytes);
            stream.Flush();
        }

        var replacementCall = Assert.Single(scope.NativeFileSystem.CreatedFiles);
        Assert.True(replacementCall.Overwrite);
        Assert.Equal(targetPath, replacementCall.Path, StringComparer.OrdinalIgnoreCase);

        var repairIndex = scope.OperationLog.FindIndex(entry => entry == $"Open:{ProgramDataManagedObjectAccess.DaclRepair}:{targetPath}");
        var createIndex = scope.OperationLog.FindIndex(entry => entry == $"CreateRelativeFile:{targetPath}");
        Assert.True(repairIndex >= 0, "Expected DACL-repair open before file replacement.");
        Assert.True(createIndex >= 0, "Expected managed file replacement call before completion.");
        Assert.True(repairIndex < createIndex, "Expected security repair to happen before managed file replacement.");

        Assert.Equal(replacementBytes, File.ReadAllBytes(targetPath));

        using var verifyHandle = scope.PathGuard.OpenExistingManagedObject(
            targetPath,
            ProgramDataObjectKind.File,
            ProgramDataManagedObjectAccess.Validate);
        scope.Verifier.VerifyFileSecurity(
            verifyHandle,
            ProgramDataFileAclProfile.PublicIconRead,
            scope.OwnerPolicyService.GetOwnerPolicy(targetPath));
    }

    [Fact]
    public void RestrictedObjectProvisioner_CreateFile_AppliesStructuredPrincipalAccess()
    {
        using var scope = new ProgramDataTestScope();
        var tempRoot = scope.Service.EnsureKnownDirectory(ProgramDataPolicies.Temp);
        var targetSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var fileParentPath = Path.Combine(tempRoot, "import");
        var filePath = Path.Combine(fileParentPath, "settings.tmp");
        Directory.CreateDirectory(fileParentPath);
        scope.State.EnsureDirectoryEntry(fileParentPath);

        scope.ObjectProvisioner.CreateFile(
            new ProgramDataExplicitFileRequest(
                filePath,
                ProgramDataFileAclProfile.CurrentProcessUserFullControl,
                [
                    new ProgramDataPrincipalAccess(
                        targetSid,
                        FileSystemRights.Read | FileSystemRights.Synchronize,
                        InheritanceFlags.None,
                        PropagationFlags.None)
                ],
                FileShare.None,
                OverwriteExisting: false),
            stream => stream.WriteByte(42));

        Assert.Equal(42, File.ReadAllBytes(filePath).Single());
        Assert.True(Directory.Exists(fileParentPath));
        var security = scope.State.GetFileSecurity(filePath);
        Assert.Equal(scope.CurrentUserSid.Value, GetOwnerSid(security).Value);
        Assert.Contains(GetExplicitRules(security), rule =>
            ((SecurityIdentifier)rule.IdentityReference).Equals(targetSid) &&
            rule.AccessControlType == AccessControlType.Allow &&
            rule.FileSystemRights == (FileSystemRights.Read | FileSystemRights.Synchronize) &&
            rule.InheritanceFlags == InheritanceFlags.None);
    }

    [Fact]
    public void RestrictedObjectProvisioner_CreateOrRepairDirectory_AppliesStructuredInheritedPrincipalAccess()
    {
        using var scope = new ProgramDataTestScope();
        var tempRoot = scope.Service.EnsureKnownDirectory(ProgramDataPolicies.Temp);
        var targetSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var directoryPath = Path.Combine(tempRoot, "export");

        scope.ObjectProvisioner.CreateOrRepairDirectory(
            new ProgramDataExplicitDirectoryRequest(
                directoryPath,
                ProgramDataDirectoryAclProfile.CurrentProcessUserFullControl,
                [
                    new ProgramDataPrincipalAccess(
                        targetSid,
                        FileSystemRights.Modify,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                        PropagationFlags.None)
                ],
                ReplaceExistingSecurity: true));

        var security = scope.State.GetDirectorySecurity(directoryPath);
        Assert.Equal(scope.CurrentUserSid.Value, GetOwnerSid(security).Value);
        Assert.Contains(GetExplicitRules(security), rule =>
            ((SecurityIdentifier)rule.IdentityReference).Equals(targetSid) &&
            rule.AccessControlType == AccessControlType.Allow &&
            rule.FileSystemRights.HasFlag(FileSystemRights.Modify) &&
            rule.InheritanceFlags == (InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit));
    }

    public static IEnumerable<object[]> PreservedManagedFileOwnerSids()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var currentSid = identity.User ?? throw new InvalidOperationException("Current test account SID was not available.");
        yield return [currentSid.Value];
        yield return [new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value];
    }

    private static DirectorySecurity CreateOwnedDirectorySecurity(SecurityIdentifier owner)
    {
        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadData,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }

    private static bool IsBuiltinUsersReadExecuteRule(FileSystemAccessRule rule)
        => IsRuleForSid(rule, WellKnownSidType.BuiltinUsersSid) &&
           rule.AccessControlType == AccessControlType.Allow &&
           (rule.FileSystemRights & FileSystemRights.ReadAndExecute) == FileSystemRights.ReadAndExecute;

    private static DirectorySecurity CreateUntrustedProtectedDirectorySecurity(SecurityIdentifier owner)
    {
        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Modify,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }

    private static DirectorySecurity CreateNonCanonicalDirectorySecurity(SecurityIdentifier owner)
    {
        var usersSid = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var acl = new RawAcl(GenericAcl.AclRevision, capacity: 2);
        acl.InsertAce(0, new CommonAce(
            AceFlags.None,
            AceQualifier.AccessAllowed,
            (int)FileSystemRights.WriteData,
            usersSid,
            isCallback: false,
            opaque: null));
        acl.InsertAce(1, new CommonAce(
            AceFlags.None,
            AceQualifier.AccessDenied,
            (int)FileSystemRights.WriteData,
            usersSid,
            isCallback: false,
            opaque: null));
        var descriptor = new RawSecurityDescriptor(
            ControlFlags.DiscretionaryAclPresent,
            owner,
            administratorsSid,
            systemAcl: null,
            discretionaryAcl: acl);
        var binary = new byte[descriptor.BinaryLength];
        descriptor.GetBinaryForm(binary, 0);

        var security = new DirectorySecurity();
        security.SetSecurityDescriptorBinaryForm(binary);
        return security;
    }

    private static FileSecurity CreateOwnedFileSecurity(SecurityIdentifier owner)
    {
        var security = new FileSecurity();
        security.SetOwner(owner);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            FileSystemRights.ReadData,
            AccessControlType.Allow));
        return security;
    }

    private static FileSecurity CreateUntrustedFileSecurity(SecurityIdentifier owner)
    {
        var security = new FileSecurity();
        security.SetOwner(owner);
        security.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            FileSystemRights.Modify,
            AccessControlType.Allow));
        return security;
    }

    private static IEnumerable<FileSystemAccessRule> GetExplicitRules(FileSystemSecurity security)
        => security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>();

    private static SecurityIdentifier GetOwnerSid(FileSystemSecurity security)
        => (SecurityIdentifier?)security.GetOwner(typeof(SecurityIdentifier))
           ?? throw new InvalidOperationException("Security owner SID was not available.");

    private static bool IsRuleForSid(FileSystemAccessRule rule, WellKnownSidType sidType)
        => rule.IdentityReference is SecurityIdentifier sid && sid.IsWellKnown(sidType);

    private static bool IsRuleForSid(FileSystemAccessRule rule, string sidValue)
        => rule.IdentityReference is SecurityIdentifier sid && sid.Value == sidValue;

    private static bool IsExactTraverseRule(FileSystemAccessRule rule, string sidValue)
        => rule.AccessControlType == AccessControlType.Allow &&
           IsRuleForSid(rule, sidValue) &&
           rule.FileSystemRights == TraverseRightsHelper.TraverseRights &&
           rule.InheritanceFlags == InheritanceFlags.None &&
           rule.PropagationFlags == PropagationFlags.None;

    private static string GetSecuritySignature(FileSystemSecurity security)
    {
        var rules = GetExplicitRules(security)
            .Select(rule =>
            {
                var sid = (SecurityIdentifier)rule.IdentityReference;
                return $"{sid.Value}|{rule.AccessControlType}|{(int)rule.FileSystemRights}|{(int)rule.InheritanceFlags}|{(int)rule.PropagationFlags}";
            })
            .OrderBy(x => x, StringComparer.Ordinal);
        return $"{security.AreAccessRulesProtected}:{GetOwnerSid(security).Value}:{string.Join(";", rules)}";
    }

    private static string GetDaclSignature(FileSystemSecurity security)
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

    private sealed class ProgramDataTestScope : IDisposable
    {
        private readonly TempDirectory tempDirectory = new("RunFence_ProgramDataSecurity");
        public ProgramDataTestScope(Mock<ILoggingService>? log = null)
        {
            RootPath = Path.Combine(tempDirectory.Path, "ProgramData");
            Directory.CreateDirectory(RootPath);
            Log = log ?? new Mock<ILoggingService>();
            CurrentUserSid = WindowsIdentity.GetCurrent().User
                ?? throw new InvalidOperationException("Current test account SID was not available.");
            State = new FakeProgramDataState(RootPath, CurrentUserSid);
            PathGuard = new FakeProgramDataPathGuard(State);
            Accessor = new FakeFileSecurityDescriptorAccessor(State);
            NativeFileSystem = new FakeBackupIntentNativeFileSystem(State);
            State.SetDirectorySecurity(RootPath, CreateRootSecurity(CurrentUserSid));
            var pathPolicyCatalog = new ProgramDataPathPolicyCatalog(PathGuard);
            var ownerPolicyService = new ProgramDataOwnerPolicyService(pathPolicyCatalog);
            var aclProfilePolicy = new ProgramDataAclProfilePolicy();
            var aclBuilder = new ProgramDataDirectoryAclBuilder(aclProfilePolicy);
            var ownerRepairService = new ProgramDataOwnerRepairService(Log.Object, Accessor, ownerPolicyService);
            var verifier = new ProgramDataSecurityVerifier(Accessor, ownerPolicyService, aclProfilePolicy);
            var applier = new ProgramDataSecurityApplier(Log.Object, Accessor, aclBuilder, pathPolicyCatalog);
            var provisioner = new ProgramDataDirectoryProvisioner(
                Log.Object,
                pathPolicyCatalog,
                PathGuard,
                aclBuilder,
                Accessor,
                applier,
                verifier,
                ownerRepairService,
                NativeFileSystem);
            var repairer = new ProgramDataManagedObjectRepairer(
                PathGuard,
                Accessor,
                ownerPolicyService,
                ownerRepairService,
                applier,
                verifier);
            var explicitAclApplier = new ProgramDataExplicitAclApplier(Log.Object, Accessor);
            var objectProvisioner = new ProgramDataObjectProvisioner(
                Log.Object,
                provisioner,
                pathPolicyCatalog,
                PathGuard,
                aclBuilder,
                Accessor,
                ownerRepairService,
                explicitAclApplier,
                NativeFileSystem);
            Service = new ProgramDataSecurityTestFacade(
                provisioner,
                objectProvisioner,
                repairer,
                PathGuard);
            ObjectProvisioner = objectProvisioner;
        }

        public string RootPath { get; }
        public Mock<ILoggingService> Log { get; }
        public SecurityIdentifier CurrentUserSid { get; }
        public FakeProgramDataState State { get; }
        public FakeProgramDataPathGuard PathGuard { get; }
        public FakeFileSecurityDescriptorAccessor Accessor { get; }
        public FakeBackupIntentNativeFileSystem NativeFileSystem { get; }
        public ProgramDataSecurityTestFacade Service { get; }
        public ProgramDataObjectProvisioner ObjectProvisioner { get; }

        public string CreateDirectory(string relativePath)
        {
            var path = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(path);
            State.EnsureDirectoryEntry(path);
            return path;
        }

        public string CreateFile(string relativePath)
        {
            var path = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path))
            {
                using var _ = File.Create(path);
            }

            State.EnsureDirectoryEntry(Path.GetDirectoryName(path)!);
            State.EnsureFileEntry(path);
            return path;
        }

        public void Dispose()
        {
            tempDirectory.Dispose();
        }

        private static DirectorySecurity CreateRootSecurity(SecurityIdentifier owner)
        {
            var security = new DirectorySecurity();
            security.SetOwner(owner);
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            return security;
        }
    }

    private sealed class FakeProgramDataPathGuard(FakeProgramDataState state) : IProgramDataPathGuard, IProgramDataPathPolicyService
    {
        private readonly HashSet<string> rejectedPaths = new(StringComparer.OrdinalIgnoreCase);
        public List<OpenCall> OpenCalls { get; } = [];

        public string NormalizeRoot() => state.RootPath;

        public string NormalizeRelativePath(string relativePath)
        {
            if (Path.IsPathRooted(relativePath))
            {
                throw new InvalidOperationException("ProgramData relative path must not be absolute.");
            }

            return Normalize(Path.Combine(state.RootPath, relativePath));
        }

        public string NormalizeAbsolutePathUnderRoot(string path)
        {
            var normalized = Normalize(path);
            EnsureUnderRoot(normalized);
            return normalized;
        }

        public string NormalizeExistingPathUnderRoot(string path, ProgramDataObjectKind kind)
        {
            var normalized = NormalizeAbsolutePathUnderRoot(path);
            RejectIfNeeded(normalized);
            return normalized;
        }

        public SafeFileHandle OpenExistingManagedObject(
            string path,
            ProgramDataObjectKind kind,
            ProgramDataManagedObjectAccess access)
        {
            var normalized = NormalizeExistingPathUnderRoot(path, kind);
            if (kind == ProgramDataObjectKind.Directory && Directory.Exists(normalized))
            {
                state.EnsureDirectoryEntry(normalized);
            }
            else if (kind == ProgramDataObjectKind.File && File.Exists(normalized))
            {
                state.EnsureFileEntry(normalized);
            }

            OpenCalls.Add(new OpenCall(normalized, kind, access));
            return state.CreateSyntheticHandle(normalized);
        }

        public bool IsUnderRoot(string path)
        {
            var normalized = Normalize(path);
            return string.Equals(normalized, state.RootPath, StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(state.RootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        public void RejectAsReparse(string path) => rejectedPaths.Add(Normalize(path));

        private void EnsureUnderRoot(string path)
        {
            if (!IsUnderRoot(path))
            {
                throw new InvalidOperationException($"Managed ProgramData path '{path}' is outside '{state.RootPath}'.");
            }
        }

        private void RejectIfNeeded(string path)
        {
            if (rejectedPaths.Contains(path))
            {
                throw new InvalidOperationException($"Managed ProgramData path '{path}' must not be a reparse point.");
            }
        }

        private static string Normalize(string path)
            => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private sealed class FakeFileSecurityDescriptorAccessor(FakeProgramDataState state)
        : IPathSecurityDescriptorAccessor, IHandleSecurityDescriptorAccessor
    {
        public int HandleModifyCallCount { get; private set; }
        public int HandleGetSecurityCallCount { get; private set; }

        public FileSystemSecurity GetSecurity(string path)
            => state.CloneSecurity(path);

        public FileSystemSecurity GetSecurity(SafeFileHandle handle, bool isDirectory)
        {
            HandleGetSecurityCallCount++;
            return state.CloneSecurity(state.GetPath(handle));
        }

        public string? GetOwnerSid(string path)
            => ProgramDataDirectorySecurityServiceTests.GetOwnerSid(state.CloneSecurity(path)).Value;

        public bool PathExists(string path, out bool isFolder)
        {
            var normalized = Path.GetFullPath(path);
            isFolder = Directory.Exists(normalized);
            return isFolder || File.Exists(normalized);
        }

        public bool ModifyAclWithFallback(string path, Func<FileSystemSecurity, bool> modify)
        {
            var security = state.CloneSecurity(path);
            bool changed = modify(security);
            if (changed)
            {
                state.SetSecurity(path, security);
            }

            return changed;
        }

        public bool ModifyAclWithFallback(SafeFileHandle handle, bool isFolder, Func<FileSystemSecurity, bool> modify)
        {
            HandleModifyCallCount++;
            var path = state.GetPath(handle);
            var security = state.CloneSecurity(path);
            bool changed = modify(security);
            if (changed)
            {
                state.SetSecurity(path, security);
            }

            return changed;
        }

        public bool ModifyOwnerAndAclWithFallback(string path, Func<FileSystemSecurity, bool> modify)
            => ModifyAclWithFallback(path, modify);

        public void SetOwnerAndAclWithFallback(string path, FileSystemSecurity security)
            => state.SetSecurity(path, security);

        public void SetOwnerWithFallback(string path, SecurityIdentifier ownerSid)
        {
            var security = state.CloneSecurity(path);
            security.SetOwner(ownerSid);
            state.SetSecurity(path, security);
        }

        public void SetOwnerWithFallback(SafeFileHandle handle, SecurityIdentifier ownerSid)
        {
            var path = state.GetPath(handle);
            var security = state.CloneSecurity(path);
            security.SetOwner(ownerSid);
            state.SetSecurity(path, security);
        }

        public void ApplyNonPropagatingAcl(string path, FileSystemSecurity security)
            => state.SetSecurity(path, security);
    }

    private sealed class FakeBackupIntentNativeFileSystem(FakeProgramDataState state) : IBackupIntentNativeFileSystem
    {
        public List<string> CreatedDirectories { get; } = [];

        public BackupIntentNativeOpenResult TryOpen(string path, bool directory)
            => new(null, 0);

        public SafeFileHandle CreateRelativeDirectory(
            SafeFileHandle parentHandle,
            string childName,
            uint desiredAccess,
            uint shareAccess,
            byte[]? securityDescriptor = null)
        {
            var parentPath = state.GetPath(parentHandle);
            var childPath = Path.Combine(parentPath, childName);
            Directory.CreateDirectory(childPath);
            state.SetSecurity(childPath, CreateSecurity(true, securityDescriptor, state.CurrentUserSid));
            CreatedDirectories.Add(childPath);
            return state.CreateSyntheticHandle(childPath);
        }

        public SafeFileHandle CreateRelativeFile(
            SafeFileHandle parentHandle,
            string childName,
            uint desiredAccess,
            uint shareAccess,
            bool overwrite,
            byte[]? securityDescriptor = null)
        {
            var parentPath = state.GetPath(parentHandle);
            var filePath = Path.Combine(parentPath, childName);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var mode = overwrite ? FileMode.Create : FileMode.CreateNew;
            var handle = File.OpenHandle(filePath, mode, FileAccess.ReadWrite, FileShare.Read);
            state.SetSecurity(filePath, CreateSecurity(false, securityDescriptor, state.CurrentUserSid));
            state.RegisterHandle(filePath, handle);
            return handle;
        }

        public bool TryEnumerateDirectories(SafeFileHandle handle, string rootPath, out IReadOnlyList<string> directories)
            => throw new NotSupportedException();

        public bool TryGetLastWriteTimeUtc(SafeFileHandle handle, out DateTime lastWriteTimeUtc)
            => throw new NotSupportedException();

        private static FileSystemSecurity CreateSecurity(bool isDirectory, byte[]? descriptor, SecurityIdentifier ownerSid)
        {
            FileSystemSecurity security = isDirectory ? new DirectorySecurity() : new FileSecurity();
            if (descriptor != null)
            {
                security.SetSecurityDescriptorBinaryForm(descriptor);
            }

            security.SetOwner(ownerSid);
            return security;
        }
    }

    private sealed class FakeProgramDataState(string rootPath, SecurityIdentifier currentUserSid)
    {
        private readonly Dictionary<string, FileSystemSecurity> entries = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<nint, string> handlePaths = new();
        private int nextSyntheticHandle = 1000;

        public string RootPath { get; } = Normalize(rootPath);
        public SecurityIdentifier CurrentUserSid { get; } = currentUserSid;

        public void EnsureDirectoryEntry(string path)
        {
            var normalized = Normalize(path);
            if (!entries.ContainsKey(normalized))
            {
                SetSecurity(normalized, DefaultDirectorySecurity());
            }
        }

        public void EnsureFileEntry(string path)
        {
            var normalized = Normalize(path);
            if (!entries.ContainsKey(normalized))
            {
                SetSecurity(normalized, DefaultFileSecurity());
            }
        }

        public void SetDirectorySecurity(string path, DirectorySecurity security)
            => SetSecurity(path, security);

        public DirectorySecurity GetDirectorySecurity(string path)
            => Assert.IsType<DirectorySecurity>(CloneSecurity(path));

        public void SetFileSecurity(string path, FileSecurity security)
            => SetSecurity(path, security);

        public FileSecurity GetFileSecurity(string path)
            => Assert.IsType<FileSecurity>(CloneSecurity(path));

        public void SetSecurity(string path, FileSystemSecurity security)
            => entries[Normalize(path)] = Clone(security);

        public FileSystemSecurity CloneSecurity(string path)
        {
            var normalized = Normalize(path);
            if (!entries.TryGetValue(normalized, out var security))
            {
                throw new InvalidOperationException($"Missing fake ProgramData security entry for '{normalized}'.");
            }

            return Clone(security);
        }

        public SafeFileHandle CreateSyntheticHandle(string path)
        {
            var handleValue = Interlocked.Increment(ref nextSyntheticHandle);
            var handle = new SafeFileHandle(new IntPtr(handleValue), ownsHandle: false);
            RegisterHandle(path, handle);
            return handle;
        }

        public void RegisterHandle(string path, SafeFileHandle handle)
            => handlePaths[handle.DangerousGetHandle()] = Normalize(path);

        public string GetPath(SafeFileHandle handle)
        {
            if (!handlePaths.TryGetValue(handle.DangerousGetHandle(), out var path))
            {
                throw new InvalidOperationException($"Unknown fake handle '{handle.DangerousGetHandle()}'.");
            }

            return path;
        }

        private DirectorySecurity DefaultDirectorySecurity()
        {
            var security = new DirectorySecurity();
            security.SetOwner(CurrentUserSid);
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            return security;
        }

        private FileSecurity DefaultFileSecurity()
        {
            var security = new FileSecurity();
            security.SetOwner(CurrentUserSid);
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));
            return security;
        }

        private static FileSystemSecurity Clone(FileSystemSecurity security)
        {
            FileSystemSecurity clone = security switch
            {
                DirectorySecurity => new DirectorySecurity(),
                FileSecurity => new FileSecurity(),
                _ => throw new InvalidOperationException($"Unsupported security type '{security.GetType().Name}'.")
            };
            clone.SetSecurityDescriptorBinaryForm(security.GetSecurityDescriptorBinaryForm());
            return clone;
        }

        private static string Normalize(string path)
            => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private readonly record struct OpenCall(
        string Path,
        ProgramDataObjectKind Kind,
        ProgramDataManagedObjectAccess Access);
}
