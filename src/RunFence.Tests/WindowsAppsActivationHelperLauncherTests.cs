using Moq;
using RunFence.Account;
using RunFence.Launch;
using RunFence.Launch.Tokens;
using Xunit;
using LaunchProcessInfo = RunFence.Launch.Tokens.ProcessInfo;

namespace RunFence.Tests;

public sealed class WindowsAppsActivationHelperLauncherTests
{
    [Fact]
    public void Launch_UsesProfileRepairWithOriginalIdentitySid_AndLaunchesHelperTargetWithResolvedIdentity()
    {
        var accountProcessLauncher = new Mock<IAccountProcessLauncher>();
        var profileRepairHelper = new Mock<IProfileRepairHelper>();
        var launcher = new WindowsAppsActivationHelperLauncher(accountProcessLauncher.Object, profileRepairHelper.Object);
        var originalIdentity = new AccountLaunchIdentity("S-1-5-21-1-2-3-1001");
        var resolvedIdentity = originalIdentity with
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.CurrentProcess)
        };
        var activationTarget = new WindowsAppsActivationTarget(
            new ProcessLaunchTarget(@"C:\RunFence\RunFence.AppxLauncher.exe", "args", HideWindow: true),
            @"C:\Temp\result",
            @"C:\Temp\result\result.jsonl",
            @"C:\Program Files\WindowsApps\Pkg\App.exe",
            "codex:");
        var processInfo = new LaunchProcessInfo(default);

        profileRepairHelper
            .Setup(h => h.ExecuteWithProfileRepair(It.IsAny<Func<LaunchProcessInfo?>>(), originalIdentity.Sid))
            .Returns<Func<LaunchProcessInfo?>, string?>((callback, _) => callback());
        accountProcessLauncher
            .Setup(l => l.Launch(activationTarget.HelperTarget, resolvedIdentity))
            .Returns(processInfo);

        using var result = launcher.Launch(activationTarget, originalIdentity, resolvedIdentity);

        Assert.NotNull(result);
        profileRepairHelper.Verify(
            h => h.ExecuteWithProfileRepair(It.IsAny<Func<LaunchProcessInfo?>>(), originalIdentity.Sid),
            Times.Once);
        accountProcessLauncher.Verify(
            l => l.Launch(activationTarget.HelperTarget, resolvedIdentity),
            Times.Once);
    }

    [Fact]
    public void Launch_WhenProcessLaunchReturnsNull_PreservesNull()
    {
        var accountProcessLauncher = new Mock<IAccountProcessLauncher>();
        var profileRepairHelper = new Mock<IProfileRepairHelper>();
        var launcher = new WindowsAppsActivationHelperLauncher(accountProcessLauncher.Object, profileRepairHelper.Object);
        var originalIdentity = new AccountLaunchIdentity("S-1-5-21-1-2-3-1001");
        var resolvedIdentity = originalIdentity with
        {
            Credentials = new LaunchCredentials(null, null, null, LaunchTokenSource.CurrentProcess)
        };
        var activationTarget = new WindowsAppsActivationTarget(
            new ProcessLaunchTarget(@"C:\RunFence\RunFence.AppxLauncher.exe", "args", HideWindow: true),
            @"C:\Temp\result",
            @"C:\Temp\result\result.jsonl",
            @"C:\Program Files\WindowsApps\Pkg\App.exe",
            "codex:");

        profileRepairHelper
            .Setup(h => h.ExecuteWithProfileRepair(It.IsAny<Func<LaunchProcessInfo?>>(), originalIdentity.Sid))
            .Returns<Func<LaunchProcessInfo?>, string?>((callback, _) => callback());
        accountProcessLauncher
            .Setup(l => l.Launch(activationTarget.HelperTarget, resolvedIdentity))
            .Returns((LaunchProcessInfo?)null);

        var result = launcher.Launch(activationTarget, originalIdentity, resolvedIdentity);

        Assert.Null(result);
    }

    [Fact]
    public void HelperProcessAdapter_Dispose_OwnsWrappedProcessLifetime()
    {
        var wrappedProcess = new LaunchProcessInfo(default);
        var adapter = new WindowsAppsActivationHelperProcessAdapter(wrappedProcess);

        adapter.Dispose();

        Assert.Throws<ObjectDisposedException>(new Action(() => _ = wrappedProcess.ExitCode));
    }
}
