using Moq;
using RunFence.Apps;
using RunFence.Core;
using RunFence.Core.Models;
using Xunit;

namespace RunFence.Tests;

public sealed class VersionedPathAutoRepairTrustPolicyTests
{
    [Theory]
    [InlineData(@"C:\Program Files\Vendor\App\App.exe")]
    [InlineData(@"C:\Program Files (x86)\Vendor\App\App.exe")]
    [InlineData(@"C:\Program Files\WindowsApps\Vendor.Tool_1.0.0.0_x64__publisher\Tool.exe")]
    public void TryCreateAutoRepairTrust_ProgramFilesRoots_AreTrusted(string exePath)
    {
        var policy = CreatePolicy(
            programFilesRoots:
            [
                @"C:\Program Files",
                @"C:\Program Files (x86)"
            ]);

        var trusted = policy.TryCreateAutoRepairTrust(new AppEntry { ExePath = exePath }, out var trust);

        Assert.True(trusted);
        Assert.False(string.IsNullOrEmpty(trust.TrustedRootPath));
    }

    [Fact]
    public void TryCreateAutoRepairTrust_EmptyProgramFilesValues_DoNotTrustUnrelatedPath()
    {
        var policy = CreatePolicy(programFilesRoots: ["", "   "]);

        var trusted = policy.TryCreateAutoRepairTrust(
            new AppEntry { ExePath = @"D:\Apps\Vendor\App.exe" },
            out _);

        Assert.False(trusted);
    }

    [Fact]
    public void TryCreateAutoRepairTrust_MatchingResolvedProfileRoot_IsTrusted()
    {
        var profiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["S-1-5-21-1"] = @"C:\Users\Alice"
        };
        var policy = CreatePolicy(profilePaths: profiles);

        var trusted = policy.TryCreateAutoRepairTrust(
            new AppEntry
            {
                ExePath = @"C:\Users\Alice\AppData\Local\Vendor\App.exe",
                AccountSid = "S-1-5-21-1"
            },
            out var trust);

        Assert.True(trusted);
        Assert.Equal(@"C:\Users\Alice", trust.TrustedRootPath);
    }

    [Theory]
    [InlineData(@"C:\Users\Bob\AppData\Local\Vendor\App.exe", "S-1-5-21-1")]
    [InlineData(@"C:\Users\Alice2\AppData\Local\Vendor\App.exe", "S-1-5-21-1")]
    [InlineData(@"C:\Users\Alice..\Vendor\App.exe", "S-1-5-21-1")]
    [InlineData(@"D:\Apps\Vendor\App.exe", "S-1-5-21-1")]
    [InlineData(@"relative\app.exe", "S-1-5-21-1")]
    [InlineData(@"C:relative\app.exe", "S-1-5-21-1")]
    [InlineData(@"::bad::path::", "S-1-5-21-1")]
    [InlineData(@"https://example.com/app.exe", "S-1-5-21-1")]
    [InlineData(@"shell:AppsFolder", "S-1-5-21-1")]
    [InlineData(@"\\server\share\Vendor\App.exe", "S-1-5-21-1")]
    [InlineData(@"\\?\C:\Users\Alice\AppData\Local\Vendor\App.exe", "S-1-5-21-1")]
    public void TryCreateAutoRepairTrust_NonMatchingOrInvalidPaths_AreNotTrusted(string exePath, string sid)
    {
        var profiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [sid] = @"C:\Users\Alice"
        };
        var policy = CreatePolicy(profilePaths: profiles);

        var trusted = policy.TryCreateAutoRepairTrust(
            new AppEntry
            {
                ExePath = exePath,
                AccountSid = sid
            },
            out _);

        Assert.False(trusted);
    }

    [Fact]
    public void TryCreateAutoRepairTrust_TextualUsersPrefixWithoutMatchingResolvedProfile_IsNotTrusted()
    {
        var profiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["S-1-5-21-1"] = @"D:\Profiles\Alice"
        };
        var policy = CreatePolicy(profilePaths: profiles);

        var trusted = policy.TryCreateAutoRepairTrust(
            new AppEntry
            {
                ExePath = @"C:\Users\Alice\AppData\Local\Vendor\App.exe",
                AccountSid = "S-1-5-21-1"
            },
            out _);

        Assert.False(trusted);
    }

    [Fact]
    public void TryCreateAutoRepairTrust_AppContainerProfilePath_IsNotTrustedByProfileRule()
    {
        var profiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["S-1-5-21-1"] = @"C:\Users\Alice"
        };
        var policy = CreatePolicy(profilePaths: profiles);

        var trusted = policy.TryCreateAutoRepairTrust(
            new AppEntry
            {
                ExePath = @"C:\Users\Alice\AppData\Local\Vendor\App.exe",
                AccountSid = "S-1-5-21-1",
                AppContainerName = "Container"
            },
            out _);

        Assert.False(trusted);
    }

    [Fact]
    public void TryCreateAutoRepairTrust_UnavailableProfileRoot_IsNotTrusted()
    {
        var policy = CreatePolicy();

        var trusted = policy.TryCreateAutoRepairTrust(
            new AppEntry
            {
                ExePath = @"C:\Users\Alice\AppData\Local\Vendor\App.exe",
                AccountSid = "S-1-5-21-1"
            },
            out _);

        Assert.False(trusted);
    }

    private static VersionedPathAutoRepairTrustPolicy CreatePolicy(
        IReadOnlyList<string>? programFilesRoots = null,
        IReadOnlyDictionary<string, string>? profilePaths = null)
    {
        var programFilesProvider = new Mock<IProgramFilesPathProvider>();
        programFilesProvider.Setup(provider => provider.GetProgramFilesRoots())
            .Returns(programFilesRoots ?? []);

        var profilePathResolver = new Mock<IProfilePathResolver>();
        profilePathResolver.Setup(resolver => resolver.TryGetProfilePath(It.IsAny<string>()))
            .Returns<string>(sid =>
                profilePaths != null && profilePaths.TryGetValue(sid, out var path) ? path : null);

        return new VersionedPathAutoRepairTrustPolicy(programFilesProvider.Object, profilePathResolver.Object);
    }
}
