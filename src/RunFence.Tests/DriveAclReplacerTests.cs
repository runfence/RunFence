using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for <see cref="DriveAclReplacer"/> — verifies <see cref="IPathGrantService"/>
/// interactions and error handling without requiring elevation or real drive-root ACL access.
/// </summary>
public class DriveAclReplacerTests : IDisposable
{
    private const string TestSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";

    private readonly Mock<IGrantSyncService> _grantSyncService = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly DriveAclReplacer _replacer;
    private readonly TempDirectory _tempDir;

    public DriveAclReplacerTests()
    {
        _replacer = new DriveAclReplacer(_grantSyncService.Object, _log.Object, AclAccessorFactory.Create());
        _tempDir = new TempDirectory("DriveAclReplacer");
    }

    public void Dispose() => _tempDir.Dispose();

    // --- UpdateFromPath is always called on success ---

    [Fact]
    public void ReplaceDriveAcl_AccessibleDirectory_CallsUpdateFromPath()
    {
        // Use a real accessible directory so GetAccessControl succeeds.
        // The temp directory has no broad-access SID ACEs, so nothing is modified —
        // UpdateFromPath must still be called regardless.

        _replacer.ReplaceDriveAcl(_tempDir.Path, TestSid);

        _grantSyncService.Verify(
            p => p.UpdateFromPath(_tempDir.Path, TestSid),
            Times.Once);
    }

    // --- Error handling ---

    [Fact]
    public void ReplaceDriveAcl_NonExistentPath_ReturnsErrorAndDoesNotCallUpdateFromPath()
    {
        var nonExistent = Path.Combine(_tempDir.Path, "does_not_exist");

        var result = _replacer.ReplaceDriveAcl(nonExistent, TestSid);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
        _grantSyncService.Verify(
            p => p.UpdateFromPath(It.IsAny<string>(), It.IsAny<string?>()),
            Times.Never);
    }

    // --- F-77: Pre-seeded broad-access ACEs exercising replacement logic ---

    private static void AddBroadAccessAce(string path, WellKnownSidType sidType, FileSystemRights rights)
    {
        var broadSid = new SecurityIdentifier(sidType, null);
        var dirInfo = new DirectoryInfo(path);
        var security = dirInfo.GetAccessControl();
        security.AddAccessRule(new FileSystemAccessRule(
            broadSid, rights,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        dirInfo.SetAccessControl(security);
    }

    private static bool HasAceForSid(string path, SecurityIdentifier sid)
    {
        var security = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        return rules.Cast<FileSystemAccessRule>().Any(r => r.IdentityReference.Equals(sid));
    }

    private static FileSystemAccessRule GetSingleAceForSid(string path, SecurityIdentifier sid)
    {
        var security = new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        return Assert.Single(rules.Cast<FileSystemAccessRule>(), rule => rule.IdentityReference.Equals(sid));
    }

    [Theory]
    [InlineData(WellKnownSidType.WorldSid)]      // Everyone
    [InlineData(WellKnownSidType.BuiltinUsersSid)] // Builtin Users
    public void ReplaceDriveAcl_WithBroadAccessAce_RemovesBroadAceAndAddsTargetSid(WellKnownSidType sidType)
    {
        // Arrange: add an explicit broad-access ACE to the temp directory
        var broadSid = new SecurityIdentifier(sidType, null);
        var targetSid = new SecurityIdentifier(TestSid);

        AddBroadAccessAce(_tempDir.Path, sidType, FileSystemRights.ReadAndExecute);
        var sourceRule = GetSingleAceForSid(_tempDir.Path, broadSid);
        Assert.True(HasAceForSid(_tempDir.Path, broadSid), "Broad-access ACE must be present before test");

        // Act
        var result = _replacer.ReplaceDriveAcl(_tempDir.Path, TestSid);

        // Assert: no error, broad ACE removed, target SID ACE added, UpdateFromPath called
        Assert.Null(result);
        Assert.False(HasAceForSid(_tempDir.Path, broadSid), "Broad-access ACE should be removed");
        var targetRule = GetSingleAceForSid(_tempDir.Path, targetSid);
        Assert.Equal(sourceRule.FileSystemRights & FileSystemRights.FullControl, targetRule.FileSystemRights);
        Assert.Equal(AccessControlType.Allow, targetRule.AccessControlType);
        _grantSyncService.Verify(p => p.UpdateFromPath(_tempDir.Path, TestSid), Times.Once);
    }

    [Fact]
    public void ReplaceDriveAcl_WhenTargetSidAceAlreadyExists_DoesNotAddDuplicateAce()
    {
        // Arrange: pre-seed both Everyone ACE and target-SID ACE with same rights
        var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var targetSid = new SecurityIdentifier(TestSid);

        AddBroadAccessAce(_tempDir.Path, WellKnownSidType.WorldSid, FileSystemRights.ReadAndExecute);
        // Also add an existing ACE for the target SID with the same rights
        var acl = AclAccessorFactory.Create();
        acl.ApplyExplicitAce(_tempDir.Path, TestSid, AccessControlType.Allow, FileSystemRights.ReadAndExecute);
        var sourceRule = GetSingleAceForSid(_tempDir.Path, everyoneSid);

        // Act
        var result = _replacer.ReplaceDriveAcl(_tempDir.Path, TestSid);

        // Assert: success, Everyone removed, target SID has exactly one ACE (no duplicate added)
        Assert.Null(result);
        Assert.False(HasAceForSid(_tempDir.Path, everyoneSid));
        var targetRule = GetSingleAceForSid(_tempDir.Path, targetSid);
        Assert.Equal(sourceRule.FileSystemRights & FileSystemRights.FullControl, targetRule.FileSystemRights);
        Assert.Equal(AccessControlType.Allow, targetRule.AccessControlType);
        Assert.Equal(sourceRule.InheritanceFlags, targetRule.InheritanceFlags);
        Assert.Equal(sourceRule.PropagationFlags, targetRule.PropagationFlags);
    }

    [Fact]
    public void HasReplaceableBroadAces_WithBroadAccessAce_ReturnsTrue()
    {
        AddBroadAccessAce(_tempDir.Path, WellKnownSidType.AuthenticatedUserSid, FileSystemRights.ReadAndExecute);

        var result = _replacer.HasReplaceableBroadAces(_tempDir.Path);

        Assert.True(result);
    }

    [Fact]
    public void HasReplaceableBroadAces_WithoutBroadAccessAce_ReturnsFalse()
    {
        var targetSid = new SecurityIdentifier(TestSid);
        AclAccessorFactory.Create().ApplyExplicitAce(
            _tempDir.Path,
            TestSid,
            AccessControlType.Allow,
            FileSystemRights.ReadAndExecute);
        Assert.True(HasAceForSid(_tempDir.Path, targetSid));

        var result = _replacer.HasReplaceableBroadAces(_tempDir.Path);

        Assert.False(result);
    }
}

