using Moq;
using RunFence.Core;
using RunFence.Launch;
using RunFence.Launching.Resolution;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsAppsActivationTargetFactoryTests
{
    [Fact]
    public void TryCreate_AppxTarget_PreservesResolvedExecutablePathAndVerbatimArguments()
    {
        var packageIdentityResolver = new Mock<IWindowsAppsPackageIdentityResolver>();
        var profilePathResolver = new Mock<IProfilePathResolver>();
        var target = new ProcessLaunchTarget(
            @"C:\Users\Test\AppData\Local\Microsoft\WindowsApps\Codex.exe",
            "--prompt \"two words\" --json");
        const string resolvedExecutablePath =
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.519.11010.0_x64__2p2nqsd0c76g0\app\Codex.exe";
        var packageResolution = new WindowsAppsPackageIdentityResolution(
            new WindowsAppsPackageIdentity(
                "OpenAI.Codex_2p2nqsd0c76g0",
                "OpenAI.Codex_26.519.11010.0_x64__2p2nqsd0c76g0"),
            resolvedExecutablePath);
        const string targetSid = "S-1-5-21-100-200-300-1001";
        packageIdentityResolver
            .Setup(r => r.TryResolvePackageIdentity(target.ExePath, out packageResolution))
            .Returns(true);
        profilePathResolver
            .Setup(r => r.TryGetProfilePath(targetSid))
            .Returns(@"C:\Users\Test");
        var factory = new WindowsAppsActivationTargetFactory(
            packageIdentityResolver.Object,
            profilePathResolver.Object,
            @"C:\RunFence\RunFence.AppxLauncher.exe");

        var result = factory.TryCreate(target, target.ExePath, targetSid);

        Assert.NotNull(result);
        Assert.Equal(resolvedExecutablePath, result.Value.AppxExecutablePath);
        Assert.Equal("--prompt \"two words\" --json", result.Value.Arguments);
        Assert.Equal(@"C:\RunFence\RunFence.AppxLauncher.exe", result.Value.HelperTarget.ExePath);
        Assert.True(result.Value.HelperTarget.HideWindow);
        Assert.True(result.Value.HelperTarget.SuppressStartupFeedback);
        Assert.StartsWith(@"C:\Users\Test\AppData\Local\RunFence\Logs\appx-launch-", result.Value.ResultDirectoryPath, StringComparison.Ordinal);
        Assert.Equal(Path.Combine(result.Value.ResultDirectoryPath, "result.jsonl"), result.Value.ResultFilePath);
        var helperArgs = CommandLineHelper.ParseProcessArguments(result.Value.HelperTarget.Arguments!);
        Assert.Equal(result.Value.ResultFilePath, helperArgs[0]);
        Assert.Equal(resolvedExecutablePath, helperArgs[1]);
        Assert.Equal(target.Arguments, CommandLineHelper.SliceVerbatimTail(result.Value.HelperTarget.Arguments!, 2));
    }

    [Fact]
    public void TryCreate_BareAliasTarget_ResolvesPackageIdentityFromAliasPath()
    {
        var packageIdentityResolver = new Mock<IWindowsAppsPackageIdentityResolver>();
        var profilePathResolver = new Mock<IProfilePathResolver>();
        var target = new ProcessLaunchTarget("wt.exe", "--new-tab");
        const string resolvedAliasPath = @"C:\Users\Test\AppData\Local\Microsoft\WindowsApps\wt.exe";
        const string resolvedExecutablePath =
            @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.23.456.0_x64__8wekyb3d8bbwe\wt.exe";
        var packageResolution = new WindowsAppsPackageIdentityResolution(
            new WindowsAppsPackageIdentity(
                "Microsoft.WindowsTerminal_8wekyb3d8bbwe",
                "Microsoft.WindowsTerminal_1.23.456.0_x64__8wekyb3d8bbwe"),
            resolvedExecutablePath);
        const string targetSid = "S-1-5-21-100-200-300-1001";
        packageIdentityResolver
            .Setup(r => r.TryResolvePackageIdentity(resolvedAliasPath, out packageResolution))
            .Returns(true);
        profilePathResolver
            .Setup(r => r.TryGetProfilePath(targetSid))
            .Returns(@"C:\Users\Test");
        var factory = new WindowsAppsActivationTargetFactory(
            packageIdentityResolver.Object,
            profilePathResolver.Object,
            @"C:\RunFence\RunFence.AppxLauncher.exe");

        var result = factory.TryCreate(target, resolvedAliasPath, targetSid);

        Assert.NotNull(result);
        Assert.Equal(resolvedExecutablePath, result.Value.AppxExecutablePath);
        Assert.Equal(target.Arguments, result.Value.Arguments);
        packageIdentityResolver.Verify(
            r => r.TryResolvePackageIdentity(resolvedAliasPath, out packageResolution),
            Times.Once);
        packageIdentityResolver.Verify(
            r => r.TryResolvePackageIdentity(target.ExePath, out It.Ref<WindowsAppsPackageIdentityResolution>.IsAny),
            Times.Never);
    }

    [Fact]
    public void TryCreate_ProfilePathUnavailableForOtherSid_ReturnsNull()
    {
        var packageIdentityResolver = new Mock<IWindowsAppsPackageIdentityResolver>();
        var profilePathResolver = new Mock<IProfilePathResolver>();
        var target = new ProcessLaunchTarget(
            @"C:\Program Files\WindowsApps\OpenAI.Codex_26.519.11010.0_x64__2p2nqsd0c76g0\app\Codex.exe");
        var packageResolution = new WindowsAppsPackageIdentityResolution(
            new WindowsAppsPackageIdentity(
                "OpenAI.Codex_2p2nqsd0c76g0",
                "OpenAI.Codex_26.519.11010.0_x64__2p2nqsd0c76g0"),
            target.ExePath);
        const string targetSid = "S-1-5-21-100-200-300-1001";
        packageIdentityResolver
            .Setup(r => r.TryResolvePackageIdentity(target.ExePath, out packageResolution))
            .Returns(true);
        profilePathResolver
            .Setup(r => r.TryGetProfilePath(targetSid))
            .Returns((string?)null);
        var factory = new WindowsAppsActivationTargetFactory(
            packageIdentityResolver.Object,
            profilePathResolver.Object,
            @"C:\RunFence\RunFence.AppxLauncher.exe");

        var result = factory.TryCreate(target, target.ExePath, targetSid);

        Assert.Null(result);
    }
}
