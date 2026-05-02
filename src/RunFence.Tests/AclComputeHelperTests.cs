using System.Security.AccessControl;
using System.Security.Principal;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class AclComputeHelperTests
{
    private const string UserSid = "S-1-5-21-1234567890-1234567890-1234567890-1001";
    private const string OtherUserSid = "S-1-5-21-1234567890-1234567890-1234567890-1002";

    private static readonly string AdministratorsSid =
        new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value;

    private static readonly string LocalSystemSid =
        new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value;

    // --- ComputeEffectiveFileRights tests ---

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
        "S-1-5-21-4024195226-107334468-2871504650-500", // Built-in Administrator (any domain)
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
}
