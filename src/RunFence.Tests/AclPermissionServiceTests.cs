using System.Security.AccessControl;
using System.Security.Principal;
using Moq;
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

    private static readonly string LocalSystemSid =
        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;

    private readonly AclPermissionService _service = new(
        new NTTranslateApi(new Mock<ILoggingService>().Object),
        new GroupMembershipApi(new Mock<ILoggingService>().Object),
        new Mock<ILocalGroupMembershipService>().Object);

    // --- ResolveAccountGroupSids tests ---

    [Fact]
    public void ResolveAccountGroupSids_ReturnsWellKnownGroupsWithoutAccountSid()
    {
        // accountSid is NOT included — callers pass it separately to HasEffectiveRights.
        // BuiltinUsers (S-1-5-32-545) is NOT hardcoded — it comes from NetUserGetLocalGroups only
        // if the account is actually a member. The fake SID is not a real account, so it won't appear.
        var result = _service.ResolveAccountGroupSids(UserSid);

        Assert.DoesNotContain(UserSid, result);
        Assert.Contains(EveryoneSid, result);
        Assert.Contains(AuthenticatedUsersSid, result);
        Assert.DoesNotContain(BuiltinUsersSid, result);
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

    // --- ComputeEffectiveFileRights tests (delegated to AclComputeHelper) ---

    [Fact]
    public void ComputeEffectiveFileRights_SingleAllowAce_ReturnsRights()
    {
        var fs = new FileSecurity();
        fs.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(UserSid), FileSystemRights.ReadAndExecute, AccessControlType.Allow));

        var result = AclComputeHelper.ComputeEffectiveFileRights(fs, (string?)null);

        Assert.True(result.ContainsKey(UserSid));
        Assert.True(result[UserSid].HasFlag(FileSystemRights.ReadAndExecute));
    }

    [Fact]
    public void ComputeEffectiveFileRights_DenyOverridesAllow()
    {
        var fs = new FileSecurity();
        var sid = new SecurityIdentifier(UserSid);
        fs.AddAccessRule(new FileSystemAccessRule(
            sid, FileSystemRights.FullControl, AccessControlType.Allow));
        fs.AddAccessRule(new FileSystemAccessRule(
            sid, FileSystemRights.Write, AccessControlType.Deny));

        var result = AclComputeHelper.ComputeEffectiveFileRights(fs, (string?)null);

        Assert.True(result.ContainsKey(UserSid));
        // Write should be subtracted from FullControl
        Assert.False(result[UserSid].HasFlag(FileSystemRights.Write));
        // ReadAndExecute should still be present
        Assert.True(result[UserSid].HasFlag(FileSystemRights.ReadAndExecute));
    }

    [Fact]
    public void ComputeEffectiveFileRights_MultipleSids_AggregatesPerSid()
    {
        var fs = new FileSecurity();
        fs.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(UserSid), FileSystemRights.Read, AccessControlType.Allow));
        fs.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(OtherUserSid), FileSystemRights.Write, AccessControlType.Allow));

        var result = AclComputeHelper.ComputeEffectiveFileRights(fs, (string?)null);

        Assert.Equal(2, result.Count);
        Assert.True(result[UserSid].HasFlag(FileSystemRights.Read));
        Assert.True(result[OtherUserSid].HasFlag(FileSystemRights.Write));
    }

    [Fact]
    public void ComputeEffectiveFileRights_MultipleAllowAces_CombinesRights()
    {
        var fs = new FileSecurity();
        var sid = new SecurityIdentifier(UserSid);
        fs.AddAccessRule(new FileSystemAccessRule(
            sid, FileSystemRights.Read, AccessControlType.Allow));
        fs.AddAccessRule(new FileSystemAccessRule(
            sid, FileSystemRights.Write, AccessControlType.Allow));

        var result = AclComputeHelper.ComputeEffectiveFileRights(fs, (string?)null);

        Assert.True(result.ContainsKey(UserSid));
        Assert.True(result[UserSid].HasFlag(FileSystemRights.Read));
        Assert.True(result[UserSid].HasFlag(FileSystemRights.Write));
    }

    [Fact]
    public void ComputeEffectiveFileRights_DenyAll_ExcludesSidFromResult()
    {
        var fs = new FileSecurity();
        var sid = new SecurityIdentifier(UserSid);
        fs.AddAccessRule(new FileSystemAccessRule(
            sid, FileSystemRights.Read, AccessControlType.Allow));
        // Deny FullControl to ensure all allowed bits (including Synchronize added by AddAccessRule) are subtracted
        fs.AddAccessRule(new FileSystemAccessRule(
            sid, FileSystemRights.FullControl, AccessControlType.Deny));

        var result = AclComputeHelper.ComputeEffectiveFileRights(fs, (string?)null);

        // Net rights are zero, so SID should not appear in result
        Assert.False(result.ContainsKey(UserSid));
    }

    [Theory]
    [MemberData(nameof(SkipTrustedSidData))]
    public void ComputeEffectiveFileRights_SkipTrustedSids_ExcludesTrustedSid(string trustedSid)
    {
        var fs = new FileSecurity();
        fs.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(trustedSid),
            FileSystemRights.FullControl, AccessControlType.Allow));
        fs.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(UserSid),
            FileSystemRights.Read, AccessControlType.Allow));

        var result = AclComputeHelper.ComputeEffectiveFileRights(fs, (string?)null, skipTrustedSids: true);

        Assert.False(result.ContainsKey(trustedSid));
        Assert.True(result.ContainsKey(UserSid));
    }

    public static TheoryData<string> SkipTrustedSidData =>
    [
        AdministratorsSid,
        LocalSystemSid,
        AclComputeHelper.CreatorOwnerSid.Value,
        AclComputeHelper.TrustedInstallerSid.Value,
        "S-1-5-80-1234567890-1234567890-1234567890-1234567890-1234567890", // NT SERVICE\*
        "S-1-5-87-1234567890-1234567890-1234567890-1234567890-1234567890", // Virtual/task identity
        "S-1-5-99-1234567890-1234567890-1234567890-1234567890-1234567890", // RESTRICTED SERVICES\*
        "S-1-5-19", // LOCAL SERVICE
        "S-1-5-20", // NETWORK SERVICE
        "S-1-5-21-4024195226-107334468-2656468696-500", // Built-in Administrator (any domain)
        "S-1-5-21-358765035-3056625321-2871504650-512", // Domain Admins (any domain)
        "S-1-5-21-100-200-300-519" // Enterprise Admins (any domain)
    ];

    [Fact]
    public void ComputeEffectiveFileRights_SkipTrustedSidsFalse_IncludesTrustedSids()
    {
        var fs = new FileSecurity();
        fs.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(AdministratorsSid),
            FileSystemRights.FullControl, AccessControlType.Allow));

        var result = AclComputeHelper.ComputeEffectiveFileRights(fs, (string?)null, skipTrustedSids: false);

        Assert.True(result.ContainsKey(AdministratorsSid));
    }

    [Fact]
    public void ComputeEffectiveFileRights_OwnerSid_ExcludedWhenSkipTrusted()
    {
        var fs = new FileSecurity();
        fs.AddAccessRule(new FileSystemAccessRule(
            new SecurityIdentifier(UserSid),
            FileSystemRights.FullControl, AccessControlType.Allow));

        var result = AclComputeHelper.ComputeEffectiveFileRights(fs, UserSid, skipTrustedSids: true);

        Assert.False(result.ContainsKey(UserSid));
    }

    [Fact]
    public void ComputeEffectiveFileRights_NoAces_ReturnsEmpty()
    {
        var fs = new FileSecurity();

        var result = AclComputeHelper.ComputeEffectiveFileRights(fs, (string?)null);

        Assert.Empty(result);
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

    // --- IsTrustedSid tests ---

    [Theory]
    [MemberData(nameof(TrustedSidData))]
    public void IsTrustedSid_WellKnownTrustedSids_ReturnsTrue(SecurityIdentifier sid)
    {
        Assert.True(AclComputeHelper.IsTrustedSid(sid, null));
    }

    public static TheoryData<SecurityIdentifier> TrustedSidData =>
    [
        new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
        new SecurityIdentifier(WellKnownSidType.CreatorOwnerSid, null),
        new SecurityIdentifier("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464"), // TrustedInstaller
        new SecurityIdentifier("S-1-5-80-1234567890-1234567890-1234567890-1234567890-1234567890"), // NT SERVICE\*
        new SecurityIdentifier("S-1-5-87-1234567890-1234567890-1234567890-1234567890-1234567890"), // Virtual/task identity
        new SecurityIdentifier("S-1-5-99-1234567890-1234567890-1234567890-1234567890-1234567890"), // RESTRICTED SERVICES\*
        new SecurityIdentifier("S-1-5-19"), // LOCAL SERVICE
        new SecurityIdentifier("S-1-5-20") // NETWORK SERVICE
    ];

    [Fact]
    public void IsTrustedSid_RegularUser_ReturnsFalse()
    {
        var sid = new SecurityIdentifier(UserSid);
        Assert.False(AclComputeHelper.IsTrustedSid(sid, null));
    }

    [Fact]
    public void IsTrustedSid_OwnerSid_ReturnsTrue()
    {
        var sid = new SecurityIdentifier(UserSid);
        Assert.True(AclComputeHelper.IsTrustedSid(sid, UserSid));
    }

    [Fact]
    public void IsTrustedSid_InvalidOwnerSid_DoesNotThrow()
    {
        var sid = new SecurityIdentifier(UserSid);
        // Invalid SID format for owner should not throw, just return false
        Assert.False(AclComputeHelper.IsTrustedSid(sid, "not-a-valid-sid"));
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

    // --- ComputeEffectiveRegistryRights tests ---

    [Fact]
    public void ComputeEffectiveRegistryRights_SingleAllowAce_ReturnsRights()
    {
        var rs = new RegistrySecurity();
        rs.AddAccessRule(new RegistryAccessRule(
            new SecurityIdentifier(UserSid), RegistryRights.ReadKey, AccessControlType.Allow));

        var result = AclComputeHelper.ComputeEffectiveRegistryRights(rs, (string?)null);

        Assert.True(result.ContainsKey(UserSid));
        Assert.True(result[UserSid].HasFlag(RegistryRights.ReadKey));
    }

    [Fact]
    public void ComputeEffectiveRegistryRights_DenyOverridesAllow()
    {
        var rs = new RegistrySecurity();
        var sid = new SecurityIdentifier(UserSid);
        rs.AddAccessRule(new RegistryAccessRule(
            sid, RegistryRights.FullControl, AccessControlType.Allow));
        rs.AddAccessRule(new RegistryAccessRule(
            sid, RegistryRights.SetValue, AccessControlType.Deny));

        var result = AclComputeHelper.ComputeEffectiveRegistryRights(rs, (string?)null);

        Assert.True(result.ContainsKey(UserSid));
        Assert.False(result[UserSid].HasFlag(RegistryRights.SetValue));
        // QueryValues should still be present
        Assert.True(result[UserSid].HasFlag(RegistryRights.QueryValues));
    }

    [Fact]
    public void ComputeEffectiveRegistryRights_SkipTrustedSids()
    {
        var rs = new RegistrySecurity();
        rs.AddAccessRule(new RegistryAccessRule(
            new SecurityIdentifier(AdministratorsSid),
            RegistryRights.FullControl, AccessControlType.Allow));
        rs.AddAccessRule(new RegistryAccessRule(
            new SecurityIdentifier(UserSid),
            RegistryRights.ReadKey, AccessControlType.Allow));

        var result = AclComputeHelper.ComputeEffectiveRegistryRights(rs, (string?)null, skipTrustedSids: true);

        Assert.False(result.ContainsKey(AdministratorsSid));
        Assert.True(result.ContainsKey(UserSid));
    }

    // --- EnsureRights tests ---

    /// <summary>
    /// Creates <c>outerDir/innerDir/</c>. The inner directory has inheritance broken;
    /// only Administrators and the current test-runner user retain FullControl, so the fake
    /// <see cref="UserSid"/> is never present and <c>NeedsPermissionGrant</c> reliably
    /// returns <c>true</c> for it.
    /// </summary>
    private static (string outerDir, string innerDir, Mock<ILoggingService> log)
        CreateProtectedTempDirectory()
    {
        var log = new Mock<ILoggingService>();
        var outerDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var innerDir = Path.Combine(outerDir, "inner");
        Directory.CreateDirectory(innerDir);

        var dirInfo = new DirectoryInfo(innerDir);
        var dirSecurity = dirInfo.GetAccessControl();
        dirSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        dirSecurity.AddAccessRule(new FileSystemAccessRule(
            AclComputeHelper.AdministratorsSid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        // Retain current-user access so the test runner can verify ACLs and clean up.
        dirSecurity.AddAccessRule(new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        dirInfo.SetAccessControl(dirSecurity);

        return (outerDir, innerDir, log);
    }

    private static bool HasReadAndExecuteAce(string dirPath, string sid)
    {
        var rules = new DirectoryInfo(dirPath)
            .GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.IdentityReference is SecurityIdentifier s && s.Value == sid &&
                rule.AccessControlType == AccessControlType.Allow &&
                rule.FileSystemRights.HasFlag(FileSystemRights.ReadAndExecute))
                return true;
        }

        return false;
    }

    [Fact]
    public void EnsureRights_ConfirmTrue_GrantsReadExecuteOnDirectory()
    {
        var (outerDir, innerDir, log) = CreateProtectedTempDirectory();
        try
        {
            int confirmCount = 0;
            bool result = _service.EnsureRights(innerDir, UserSid, FileSystemRights.ReadAndExecute, log.Object,
                _ =>
                {
                    confirmCount++;
                    return true;
                });

            Assert.True(result);
            Assert.Equal(1, confirmCount);
            Assert.True(HasReadAndExecuteAce(innerDir, UserSid),
                "UserSid should have ReadAndExecute ACE on the directory");
        }
        finally
        {
            Directory.Delete(outerDir, true);
        }
    }

    [Fact]
    public void EnsureRights_DirectoryPath_GrantsOnDirectoryItself()
    {
        var (outerDir, innerDir, log) = CreateProtectedTempDirectory();
        try
        {
            bool result = _service.EnsureRights(innerDir, UserSid, FileSystemRights.ReadAndExecute, log.Object, _ => true);

            Assert.True(result);
            Assert.True(HasReadAndExecuteAce(innerDir, UserSid),
                "UserSid should have ReadAndExecute ACE when a directory path is passed directly");
        }
        finally
        {
            Directory.Delete(outerDir, true);
        }
    }

    [Fact]
    public void EnsureRights_ConfirmFalse_NoAceAdded()
    {
        var (outerDir, innerDir, log) = CreateProtectedTempDirectory();
        try
        {
            bool result = _service.EnsureRights(innerDir, UserSid, FileSystemRights.ReadAndExecute, log.Object, _ => false);

            Assert.False(result);
            Assert.False(HasReadAndExecuteAce(innerDir, UserSid),
                "UserSid should not have an ACE when confirm returns false");
        }
        finally
        {
            Directory.Delete(outerDir, true);
        }
    }

    [Fact]
    public void EnsureRights_ConfirmThrowsOce_PropagatesAndNoAceAdded()
    {
        var (outerDir, innerDir, log) = CreateProtectedTempDirectory();
        try
        {
            Assert.Throws<OperationCanceledException>(() =>
                _service.EnsureRights(innerDir, UserSid, FileSystemRights.ReadAndExecute, log.Object,
                    _ => throw new OperationCanceledException()));

            Assert.False(HasReadAndExecuteAce(innerDir, UserSid),
                "UserSid should not have an ACE when confirm throws OCE");
        }
        finally
        {
            Directory.Delete(outerDir, true);
        }
    }

    [Fact]
    public void EnsureRights_AlreadyHasPermission_ReturnsFalseNoConfirmNeeded()
    {
        var (outerDir, innerDir, log) = CreateProtectedTempDirectory();
        try
        {
            // Pre-grant ReadAndExecute on innerDir
            _service.GrantRights(innerDir, UserSid);

            bool confirmCalled = false;
            bool result = _service.EnsureRights(innerDir, UserSid, FileSystemRights.ReadAndExecute, log.Object,
                _ =>
                {
                    confirmCalled = true;
                    return true;
                });

            Assert.False(result, "Should return false when access is already sufficient");
            Assert.False(confirmCalled, "No confirm should be needed when account already has ReadAndExecute");
        }
        finally
        {
            Directory.Delete(outerDir, true);
        }
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
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        try
        {
            var ancestors = _service.GetGrantableAncestors(tempDir);

            Assert.NotEmpty(ancestors);
            // When path is a directory, first element is the directory itself
            Assert.Equal(tempDir, ancestors[0]);
        }
        finally
        {
            Directory.Delete(tempDir);
        }
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