using System.Security.Principal;
using RunFence.Acl;
using Xunit;

namespace RunFence.Tests;

public class ProgramDataOwnerPolicyServiceTests
{
    [Fact]
    public void GetPreferredRepairOwnerSid_ReturnsCurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var currentSid = identity.User ?? throw new InvalidOperationException("Current test account SID was not available.");
        var service = CreateService();

        Assert.Equal(currentSid.Value, service.GetPreferredRepairOwnerSid().Value);
    }

    [Theory]
    [InlineData(@"C:\ProgramData\RunFence", ProgramDataAllowedOwnerPolicy.AdministratorsOrCurrentAccount)]
    [InlineData(@"C:\ProgramData\RunFence\PackageInstallScripts", ProgramDataAllowedOwnerPolicy.AdministratorsOrCurrentAccount)]
    [InlineData(@"C:\ProgramData\RunFence\scripts", ProgramDataAllowedOwnerPolicy.AdministratorsOrCurrentAccount)]
    [InlineData(@"C:\ProgramData\RunFence\temp", ProgramDataAllowedOwnerPolicy.AdministratorsOrCurrentAccount)]
    [InlineData(@"C:\ProgramData\RunFence\PrefTransLogs", ProgramDataAllowedOwnerPolicy.AdministratorsOrCurrentAccount)]
    [InlineData(@"C:\ProgramData\RunFence\icons", ProgramDataAllowedOwnerPolicy.AdministratorsOrCurrentAccount)]
    [InlineData(@"C:\ProgramData\RunFence\AC", ProgramDataAllowedOwnerPolicy.AdministratorsCurrentAccountWithExpectedOwners)]
    [InlineData(@"C:\ProgramData\RunFence\AC\ProfileA", ProgramDataAllowedOwnerPolicy.AdministratorsCurrentAccountWithExpectedOwners)]
    public void GetOwnerPolicy_ReturnsCatalogPolicy(string path, ProgramDataAllowedOwnerPolicy expectedPolicy)
    {
        var service = CreateService();

        Assert.Equal(expectedPolicy, service.GetOwnerPolicy(path));
    }

    [Theory]
    [InlineData(WellKnownSidType.BuiltinAdministratorsSid, ProgramDataAllowedOwnerPolicy.AdministratorsOrCurrentAccount, true)]
    [InlineData(WellKnownSidType.BuiltinAdministratorsSid, ProgramDataAllowedOwnerPolicy.AdministratorsCurrentAccountWithExpectedOwners, true)]
    public void IsAllowedOwner_AllowsAdministratorsForEveryPolicy(
        WellKnownSidType sidType,
        ProgramDataAllowedOwnerPolicy policy,
        bool expected)
    {
        var service = CreateService();
        var ownerSid = new SecurityIdentifier(sidType, null);

        Assert.Equal(expected, service.IsAllowedOwner(ownerSid, policy));
    }

    [Fact]
    public void IsAllowedOwner_AllowsPreferredRepairOwner()
    {
        var service = CreateService();

        Assert.True(service.IsAllowedOwner(
            service.GetPreferredRepairOwnerSid(),
            ProgramDataAllowedOwnerPolicy.AdministratorsOrCurrentAccount));
    }

    [Fact]
    public void IsAllowedOwner_WithExpectedOwnersPolicy_AllowsOnlyListedAdditionalOwner()
    {
        var expectedPackageSid = new SecurityIdentifier("S-1-15-2-1");
        var unexpectedPackageSid = new SecurityIdentifier("S-1-15-2-2");
        var service = CreateService();

        Assert.True(service.IsAllowedOwner(
            expectedPackageSid,
            ProgramDataAllowedOwnerPolicy.AdministratorsCurrentAccountWithExpectedOwners,
            [expectedPackageSid]));
        Assert.False(service.IsAllowedOwner(
            unexpectedPackageSid,
            ProgramDataAllowedOwnerPolicy.AdministratorsCurrentAccountWithExpectedOwners,
            [expectedPackageSid]));
    }

    [Fact]
    public void IsAllowedOwner_WithoutExpectedOwnersPolicy_RejectsNonPreferredNonAdministratorOwner()
    {
        var service = CreateService();
        var otherSid = new SecurityIdentifier("S-1-5-21-9999-9999-9999-1001");

        Assert.False(service.IsAllowedOwner(
            otherSid,
            ProgramDataAllowedOwnerPolicy.AdministratorsOrCurrentAccount));
    }

    private static ProgramDataOwnerPolicyService CreateService()
        => new(new ProgramDataPathPolicyCatalog(new FakeProgramDataPathGuard()));

    private sealed class FakeProgramDataPathGuard : IProgramDataPathGuard
    {
        public string NormalizeRoot() => @"C:\ProgramData\RunFence";

        public string NormalizeRelativePath(string relativePath) => Path.Combine(NormalizeRoot(), relativePath);

        public string NormalizeAbsolutePathUnderRoot(string path)
        {
            if (!Path.IsPathFullyQualified(path))
            {
                throw new InvalidOperationException("Managed ProgramData path must be fully qualified.");
            }

            var normalized = Normalize(path);
            if (!IsUnderRoot(normalized))
            {
                throw new InvalidOperationException($"Managed ProgramData path '{normalized}' is outside '{NormalizeRoot()}'.");
            }

            return normalized;
        }

        public string NormalizeExistingPathUnderRoot(string path, ProgramDataObjectKind kind)
            => NormalizeAbsolutePathUnderRoot(path);

        public Microsoft.Win32.SafeHandles.SafeFileHandle OpenExistingManagedObject(
            string path,
            ProgramDataObjectKind kind,
            ProgramDataManagedObjectAccess access)
            => throw new NotSupportedException();

        public bool IsUnderRoot(string path)
        {
            if (!Path.IsPathFullyQualified(path))
            {
                return false;
            }

            var normalized = Normalize(path);
            var root = NormalizeRoot();
            return string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string path)
            => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
