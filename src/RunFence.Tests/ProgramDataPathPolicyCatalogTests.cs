using RunFence.Acl;
using RunFence.Core;
using Xunit;

namespace RunFence.Tests;

public class ProgramDataPathPolicyCatalogTests
{
    [Fact]
    public void ResolveKnownDirectoryProfile_RootHasNoManagedProfile_AndIconsUseFixedProfile()
    {
        var catalog = CreateCatalog();

        Assert.Null(catalog.ResolveKnownDirectoryProfile(catalog.RootPath));
        Assert.False(catalog.IsKnownManagedDirectory(catalog.RootPath));
        Assert.Equal(
            ProgramDataDirectoryAclProfile.PublicReadTrustedWrite,
            catalog.ResolveKnownDirectoryProfile(Path.Combine(catalog.RootPath, ProgramDataPolicies.Icons.RelativePath)));
    }

    [Fact]
    public void ResolveOwnerPolicy_AcTreeUsesExpectedOwnersPolicy()
    {
        var catalog = CreateCatalog();

        Assert.Equal(
            ProgramDataAllowedOwnerPolicy.AdministratorsCurrentAccountWithExpectedOwners,
            catalog.ResolveOwnerPolicy(Path.Combine(catalog.RootPath, ProgramDataPolicies.Ac.RelativePath)));
        Assert.Equal(
            ProgramDataAllowedOwnerPolicy.AdministratorsCurrentAccountWithExpectedOwners,
            catalog.ResolveOwnerPolicy(Path.Combine(catalog.RootPath, ProgramDataPolicies.Ac.RelativePath, "ProfileA")));
    }

    [Fact]
    public void ResolveKnownDirectoryProfile_UnknownTopLevelDirectoryHasNoProfile()
    {
        var catalog = CreateCatalog();
        var unknownTopLevelDirectory = Path.Combine(catalog.RootPath, "ShortcutProtectionState");

        Assert.Null(catalog.ResolveKnownDirectoryProfile(unknownTopLevelDirectory));
    }

    [Fact]
    public void RegisterDirectoryProfile_UnknownTopLevelDirectoryIsRejected()
    {
        var catalog = CreateCatalog();
        var unknownTopLevelDirectory = Path.Combine(catalog.RootPath, "ShortcutProtectionState");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            catalog.RegisterDirectoryProfile(unknownTopLevelDirectory, ProgramDataDirectoryAclProfile.PublicReadTrustedWrite));

        Assert.Contains("known caller-owned ProgramData subtree", exception.Message);
    }

    [Fact]
    public void RegisterDirectoryProfile_NonDynamicKnownSubtreeIsRejected()
    {
        var catalog = CreateCatalog();
        var scriptsChildPath = Path.Combine(catalog.RootPath, ProgramDataPolicies.Scripts.RelativePath, "child");

        Assert.Null(catalog.ResolveKnownDirectoryProfile(scriptsChildPath));
        var exception = Assert.Throws<InvalidOperationException>(() =>
            catalog.RegisterDirectoryProfile(scriptsChildPath, ProgramDataDirectoryAclProfile.TrustedOnly));

        Assert.Contains("known caller-owned ProgramData subtree", exception.Message);
    }

    [Fact]
    public void RegisterDirectoryProfile_DynamicSubtreeAllowsSameProfile_AndThrowsOnConflict()
    {
        var catalog = CreateCatalog();
        var dynamicPath = Path.Combine(
            catalog.RootPath,
            ProgramDataPolicies.WindowsTerminalDeploymentWork.RelativePath,
            "operation",
            "staging");

        Assert.Equal(
            ProgramDataDirectoryAclProfile.SharedExecutableReadExecute,
            catalog.RegisterDirectoryProfile(dynamicPath, ProgramDataDirectoryAclProfile.SharedExecutableReadExecute));
        Assert.Equal(
            ProgramDataDirectoryAclProfile.SharedExecutableReadExecute,
            catalog.RegisterDirectoryProfile(dynamicPath, ProgramDataDirectoryAclProfile.SharedExecutableReadExecute));
        Assert.Equal(
            ProgramDataDirectoryAclProfile.SharedExecutableReadExecute,
            catalog.ResolveKnownDirectoryProfile(dynamicPath));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            catalog.RegisterDirectoryProfile(dynamicPath, ProgramDataDirectoryAclProfile.TrustedOnly));

        Assert.Contains("already registered with ACL profile", exception.Message);
    }

    [Fact]
    public void ResolveKnownDirectoryProfile_UnregisteredDynamicChildHasNoProfile()
    {
        var catalog = CreateCatalog();
        var dynamicPath = Path.Combine(
            catalog.RootPath,
            ProgramDataPolicies.WindowsTerminalDeploymentWork.RelativePath,
            "operation");

        Assert.Null(catalog.ResolveKnownDirectoryProfile(dynamicPath));
        Assert.True(catalog.IsUnderDynamicDirectoryPolicyRoot(dynamicPath));
    }

    [Fact]
    public void RegisterDirectoryProfile_KnownPathThrowsWhenCallerRequestsDifferentProfile()
    {
        var catalog = CreateCatalog();
        var iconsPath = Path.Combine(catalog.RootPath, ProgramDataPolicies.Icons.RelativePath);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            catalog.RegisterDirectoryProfile(iconsPath, ProgramDataDirectoryAclProfile.TrustedOnly));

        Assert.Contains("fixed ACL profile", exception.Message);
    }

    [Theory]
    [MemberData(nameof(KnownManagedDirectories))]
    public void KnownManagedDirectories_AreRecognized_AndResolveExpectedProfile(
        string relativePath,
        ProgramDataDirectoryAclProfile expectedProfile)
    {
        var catalog = CreateCatalog();
        var absolutePath = Path.Combine(catalog.RootPath, relativePath);

        Assert.True(catalog.IsKnownManagedDirectory(absolutePath));
        catalog.RegisterDirectoryProfile(absolutePath, expectedProfile);
        Assert.Equal(expectedProfile, catalog.ResolveKnownDirectoryProfile(absolutePath));
    }

    [Theory]
    [MemberData(nameof(OwnerPolicyPaths))]
    public void ResolveOwnerPolicy_ReturnsExpectedPolicyForManagedPaths(string relativePath, ProgramDataAllowedOwnerPolicy expectedPolicy)
    {
        var catalog = CreateCatalog();

        Assert.Equal(expectedPolicy, catalog.ResolveOwnerPolicy(Path.Combine(catalog.RootPath, relativePath)));
    }

    public static IEnumerable<object[]> KnownManagedDirectories()
    {
        foreach (var policy in ProgramDataPolicies.Directories)
        {
            yield return [policy.RelativePath, policy.Profile];
        }
    }

    public static IEnumerable<object[]> OwnerPolicyPaths()
    {
        yield return [string.Empty, ProgramDataAllowedOwnerPolicy.AdministratorsOrCurrentAccount];
        foreach (var policy in ProgramDataPolicies.Directories.Where(policy => policy != ProgramDataPolicies.Ac))
        {
            yield return [policy.RelativePath, ProgramDataAllowedOwnerPolicy.AdministratorsOrCurrentAccount];
        }

        yield return [ProgramDataPolicies.Ac.RelativePath, ProgramDataAllowedOwnerPolicy.AdministratorsCurrentAccountWithExpectedOwners];
        yield return [Path.Combine(ProgramDataPolicies.Ac.RelativePath, "ProfileA"), ProgramDataAllowedOwnerPolicy.AdministratorsCurrentAccountWithExpectedOwners];
    }

    private static ProgramDataPathPolicyCatalog CreateCatalog()
        => new(new FakeProgramDataPathGuard());

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
