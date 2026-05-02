using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
using RunFence.Acl;
using RunFence.Acl.Permissions;
using RunFence.Core;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class AclPermissionServiceTests
{
    // Fake user SID (non-trusted, non-well-known)
    private const string UserSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string OtherUserSid = "S-1-5-21-1234567890-1234567890-1234567890-1002";

    // Well-known SIDs
    private static readonly string EveryoneSid = "S-1-1-0";
    private static readonly string AuthenticatedUsersSid = "S-1-5-11";
    private static readonly string BuiltinUsersSid = "S-1-5-32-545";

    private static readonly string AdministratorsSid =
        new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value;

    private readonly AclPermissionService _service = new(
        new NTTranslateApi(new Mock<ILoggingService>().Object),
        new GroupMembershipApi(new Mock<ILoggingService>().Object),
        new Mock<ILocalGroupMembershipService>().Object,
        new AclAccessor());

    // --- ResolveAccountGroupSids tests ---

    [Fact]
    public void ResolveAccountGroupSids_ReturnsWellKnownGroupsWithoutAccountSid()
    {
        // accountSid is NOT included — callers pass it separately to HasEffectiveRights.
        // BuiltinUsers (S-1-5-32-545) is hardcoded: every authenticated user's token always
        // includes it (LSA injects Authenticated Users unconditionally, and Authenticated Users
        // is always a member of BUILTIN\Users via SamrGetAliasMembership).
        var result = _service.ResolveAccountGroupSids(UserSid);

        Assert.DoesNotContain(UserSid, result);
        Assert.Contains(EveryoneSid, result);
        Assert.Contains(AuthenticatedUsersSid, result);
        Assert.Contains(BuiltinUsersSid, result);
    }

    [Fact]
    public void ResolveAccountGroupSids_EveryoneIsFirst()
    {
        var result = _service.ResolveAccountGroupSids(UserSid);

        Assert.Equal(EveryoneSid, result[0]);
    }

    [Fact]
    public void ResolveAccountGroupSids_AppContainerSid_ReturnsEveryoneAndAllApplicationPackages()
    {
        // AppContainer SIDs (S-1-15-2-*) have fixed group membership:
        // Everyone + ALL_APPLICATION_PACKAGES only. NetUserGetLocalGroups is not called.
        const string appContainerSid = "S-1-15-2-99";
        const string allAppPackagesSid = "S-1-15-2-1";

        var result = _service.ResolveAccountGroupSids(appContainerSid);

        Assert.Equal(2, result.Count);
        Assert.Contains(EveryoneSid, result, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(allAppPackagesSid, result, StringComparer.OrdinalIgnoreCase);
    }

    // --- HasEffectiveRights tests ---

    [Fact]
    public void HasEffectiveRights_AllowAceGrantsRequiredRights_ReturnsTrue()
    {
        var fs = new FileSecurity();
        fs.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(UserSid),
            FileSystemRights.ReadAndExecute, AccessControlType.Allow));

        var groupSids = _service.ResolveAccountGroupSids(UserSid);

        Assert.True(_service.HasEffectiveRights(
            fs, UserSid, groupSids, FileSystemRights.ReadAndExecute));
    }

    [Fact]
    public void HasEffectiveRights_DenyAceOverrides_ReturnsFalse()
    {
        var fs = new FileSecurity();
        var sid = new SecurityIdentifier(UserSid);
        fs.AddAccessRule(new FileSystemAccessRule(
            sid, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        fs.AddAccessRule(new FileSystemAccessRule(
            sid, FileSystemRights.ReadAndExecute, AccessControlType.Deny));

        var groupSids = _service.ResolveAccountGroupSids(UserSid);

        Assert.False(_service.HasEffectiveRights(
            fs, UserSid, groupSids, FileSystemRights.ReadAndExecute));
    }

    [Fact]
    public void HasEffectiveRights_GroupSidGrantsRights_ReturnsTrue()
    {
        var fs = new FileSecurity();
        // Grant rights via Everyone group rather than the account SID directly
        fs.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(EveryoneSid),
            FileSystemRights.ReadAndExecute, AccessControlType.Allow));

        var groupSids = _service.ResolveAccountGroupSids(UserSid);

        Assert.True(_service.HasEffectiveRights(
            fs, UserSid, groupSids, FileSystemRights.ReadAndExecute));
    }

    [Fact]
    public void HasEffectiveRights_NoMatchingAces_ReturnsFalse()
    {
        var fs = new FileSecurity();
        // Only OtherUser has rights
        fs.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(OtherUserSid),
            FileSystemRights.ReadAndExecute, AccessControlType.Allow));

        var groupSids = _service.ResolveAccountGroupSids(UserSid);

        Assert.False(_service.HasEffectiveRights(
            fs, UserSid, groupSids, FileSystemRights.ReadAndExecute));
    }

    [Fact]
    public void HasEffectiveRights_PartialRights_ReturnsFalse()
    {
        var fs = new FileSecurity();
        // Grant Read only, but require ReadAndExecute
        fs.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(UserSid),
            FileSystemRights.Read, AccessControlType.Allow));

        var groupSids = _service.ResolveAccountGroupSids(UserSid);

        Assert.False(_service.HasEffectiveRights(
            fs, UserSid, groupSids, FileSystemRights.ReadAndExecute));
    }

    [Fact]
    public void HasEffectiveRights_RightsFromAccountAndGroup_AggregatedTogether()
    {
        var fs = new FileSecurity();
        // Account SID has Read, Everyone group has ExecuteFile
        fs.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(UserSid),
            FileSystemRights.Read, AccessControlType.Allow));
        fs.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(EveryoneSid),
            FileSystemRights.ExecuteFile, AccessControlType.Allow));

        var groupSids = _service.ResolveAccountGroupSids(UserSid);

        // Combined should satisfy Read | ExecuteFile
        Assert.True(_service.HasEffectiveRights(
            fs, UserSid, groupSids, FileSystemRights.Read | FileSystemRights.ExecuteFile));
    }

    [Fact]
    public void HasEffectiveRights_TrustedSidIncluded_SkipTrustedFalse()
    {
        // HasEffectiveRights calls ComputeEffectiveFileRights with skipTrustedSids: false,
        // so Administrators SID in the group list should count
        var fs = new FileSecurity();
        fs.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(AdministratorsSid),
            FileSystemRights.FullControl, AccessControlType.Allow));

        // Include Administrators in the group SIDs
        var groupSids = new List<string> { UserSid, AdministratorsSid };

        Assert.True(_service.HasEffectiveRights(
            fs, UserSid, groupSids, FileSystemRights.ReadAndExecute));
    }

    [Fact]
    public void NeedsPermissionGrant_Unelevated_AdminsGroupExcludedFromCheck()
    {
        // When unelevated=true, the Administrators group SID must be excluded from the effective-rights
        // check. This is tested through HasEffectiveRights: verifying that access granted to Admins
        // is NOT seen when Admins is removed from groupSids (simulating unelevated token filtering).
        var fs = new FileSecurity();
        fs.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(AdministratorsSid),
            FileSystemRights.ReadAndExecute, AccessControlType.Allow));

        // With Admins in groupSids → access is granted
        var groupSidsWithAdmins = new List<string> { EveryoneSid, AuthenticatedUsersSid, AdministratorsSid };
        Assert.True(_service.HasEffectiveRights(fs, UserSid, groupSidsWithAdmins, FileSystemRights.ReadAndExecute));

        // Without Admins in groupSids → no access (simulates what unelevated=true does)
        var groupSidsWithoutAdmins = new List<string> { EveryoneSid, AuthenticatedUsersSid };
        Assert.False(_service.HasEffectiveRights(fs, UserSid, groupSidsWithoutAdmins, FileSystemRights.ReadAndExecute));
    }

    [Fact]
    public void HasEffectiveRights_DenyOnGroupSid_DoesNotOverrideAllowOnAccountSid()
    {
        // Documents intentional simplification: per-SID deny does not cross SID boundaries.
        // Real Windows evaluates deny from ANY token SID before allow from ANY.
        var fs = new FileSecurity();
        fs.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(UserSid), FileSystemRights.ReadAndExecute, AccessControlType.Allow));
        fs.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(EveryoneSid), FileSystemRights.ReadAndExecute, AccessControlType.Deny));

        var groupSids = _service.ResolveAccountGroupSids(UserSid);

        Assert.True(_service.HasEffectiveRights(
            fs, UserSid, groupSids, FileSystemRights.ReadAndExecute));
    }

    // --- GetGrantableAncestors tests ---

    [Fact]
    public void GetGrantableAncestors_FileInTempDir_ReturnsTempDirAndAncestors()
    {
        // Use a temp path — temp dir is not a blocked ACL root
        var tempFile = Path.Combine(Path.GetTempPath(), "subdir", "test.exe");
        var ancestors = _service.GetGrantableAncestors(tempFile);

        // The immediate parent and at least one more ancestor should be present
        Assert.NotEmpty(ancestors);
        Assert.Equal(Path.Combine(Path.GetTempPath(), "subdir"), ancestors[0]);
        Assert.Contains(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), ancestors);
    }

    [Fact]
    public void GetGrantableAncestors_FileInWindowsDir_ReturnsEmpty()
    {
        // Windows dir is a blocked ACL root — immediate parent IS the blocked root
        var windowsFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");
        var ancestors = _service.GetGrantableAncestors(windowsFile);

        Assert.Empty(ancestors);
    }

    [Fact]
    public void GetGrantableAncestors_FileInSubdirOfWindowsDir_ExcludesWindowsRoot()
    {
        // system32 is a blocked ACL path — immediate parent is a blocked root
        var sysFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), "kernel32.dll");
        var ancestors = _service.GetGrantableAncestors(sysFile);

        // System32 itself is a blocked root so nothing grantable
        Assert.Empty(ancestors);
    }

    [Fact]
    public void GetGrantableAncestors_DirectoryPath_StartsWithDirectoryItself()
    {
        using var tempDir = new TempDirectory("RunFence_AclPermTest");
        var ancestors = _service.GetGrantableAncestors(tempDir.Path);

        Assert.NotEmpty(ancestors);
        // When path is a directory, first element is the directory itself
        Assert.Equal(tempDir.Path, ancestors[0]);
    }

    [Fact]
    public void GetGrantableAncestors_NeverIncludesDriveRoot()
    {
        // Even on a non-system drive, the drive root should not appear
        var tempFile = Path.Combine(Path.GetTempPath(), "a", "b", "c.exe");
        var ancestors = _service.GetGrantableAncestors(tempFile);

        // No ancestor should be a path root (single-segment like "C:\")
        foreach (var a in ancestors)
            Assert.NotEqual(Path.GetPathRoot(a), a, StringComparer.OrdinalIgnoreCase);
    }
}