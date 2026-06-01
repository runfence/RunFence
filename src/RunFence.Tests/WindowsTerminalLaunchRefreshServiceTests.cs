using System.Threading;
using Moq;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Persistence;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsTerminalLaunchRefreshServiceTests : IDisposable
{
    private const string TestSid = "S-1-5-21-100-200-300-1001";
    private readonly TempDirectory _tempDirectory = new("RunFence_WindowsTerminalLaunchRefresh");

    [Fact]
    public async Task EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync_WhenSharedDeploymentAlreadyExists_DoesNotRunDeployment()
    {
        var context = new TestContext(_tempDirectory.Path);
        Directory.CreateDirectory(context.DeploymentPaths.SharedRootPath);
        File.WriteAllText(context.DeploymentPaths.SharedExecutablePath, string.Empty);

        await context.Service.EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync(new AccountLaunchIdentity(TestSid));

        context.ProgressRunner.Verify(
            runner => runner.RunAsync(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>()),
            Times.Never);
        context.DeploymentService.Verify(
            service => service.EnsureSharedDeploymentReadyAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync_WhenSharedDeploymentIsMissing_RunsDeployment()
    {
        var context = new TestContext(_tempDirectory.Path);

        await context.Service.EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync(new AccountLaunchIdentity(TestSid));

        context.ProgressRunner.Verify(
            runner => runner.RunAsync(
                "Installing standalone Windows Terminal from official GitHub...",
                It.IsAny<Func<CancellationToken, Task>>()),
            Times.Once);
        context.DeploymentService.Verify(
            service => service.EnsureSharedDeploymentReadyAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync_WhenAppIsLocked_DoesNotRunDeployment()
    {
        var context = new TestContext(_tempDirectory.Path);
        context.AppLock.Setup(control => control.IsLocked).Returns(true);

        await context.Service.EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync(new AccountLaunchIdentity(TestSid));

        context.ProgressRunner.Verify(
            runner => runner.RunAsync(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>()),
            Times.Never);
        context.DeploymentService.Verify(
            service => service.EnsureSharedDeploymentReadyAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync_WhenAppLocksBeforeDeploymentStarts_DoesNotRunDeployment()
    {
        var context = new TestContext(_tempDirectory.Path);
        context.AppLock
            .SetupSequence(control => control.IsLocked)
            .Returns(false)
            .Returns(true);

        await context.Service.EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync(new AccountLaunchIdentity(TestSid));

        context.ProgressRunner.Verify(
            runner => runner.RunAsync(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task>>()),
            Times.Once);
        context.DeploymentService.Verify(
            service => service.EnsureSharedDeploymentReadyAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync_WhenDeploymentFails_CompletesWithoutThrowing()
    {
        var context = new TestContext(_tempDirectory.Path);
        context.DeploymentService
            .Setup(service => service.EnsureSharedDeploymentReadyAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("deploy failed"));

        await context.Service.EnsureSharedDeploymentExistsBeforeTerminalLaunchAsync(new AccountLaunchIdentity(TestSid));

        context.DeploymentService.Verify(
            service => service.EnsureSharedDeploymentReadyAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void TryStartOnlineRefreshAfterTerminalLaunch_WhenLaunchTargetIsNotShared_DoesNothing()
    {
        var context = new TestContext(_tempDirectory.Path);
        context.AccountState.Setup(service => service.ResolveLaunchTarget(It.IsAny<AccountLaunchIdentity>())).Returns("cmd.exe");

        context.Service.TryStartOnlineRefreshAfterTerminalLaunch(new AccountLaunchIdentity(TestSid));

        context.DeploymentService.Verify(
            service => service.TryDeployLatestCachedZipIfNewerThanSharedAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        context.DeploymentService.Verify(
            service => service.EnsureLatestReleaseCachedAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void TryStartOnlineRefreshAfterTerminalLaunch_WhenAppIsLocked_DoesNotStartCachedDeployOrOnlineRefresh()
    {
        var context = new TestContext(_tempDirectory.Path);
        context.AppLock.Setup(control => control.IsLocked).Returns(true);

        context.Service.TryStartOnlineRefreshAfterTerminalLaunch(new AccountLaunchIdentity(TestSid));

        context.DeploymentService.Verify(
            service => service.TryDeployLatestCachedZipIfNewerThanSharedAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        context.DeploymentService.Verify(
            service => service.EnsureLatestReleaseCachedAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        context.SessionSaver.Verify(saver => saver.SaveConfig(), Times.Never);
    }

    [Fact]
    public void TryStartOnlineRefreshAfterTerminalLaunch_WhenAppLocksBeforeBackgroundWorkStarts_DoesNotStartCachedDeployOrOnlineRefresh()
    {
        var context = new TestContext(_tempDirectory.Path);
        using var backgroundLockRead = new ManualResetEventSlim();
        var lockReadCount = 0;
        context.AppLock
            .Setup(control => control.IsLocked)
            .Returns(() =>
            {
                if (Interlocked.Increment(ref lockReadCount) == 1)
                    return false;

                backgroundLockRead.Set();
                return true;
            });

        context.Service.TryStartOnlineRefreshAfterTerminalLaunch(new AccountLaunchIdentity(TestSid));

        Assert.True(backgroundLockRead.Wait(TimeSpan.FromSeconds(5)));
        context.DeploymentService.Verify(
            service => service.TryDeployLatestCachedZipIfNewerThanSharedAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        context.DeploymentService.Verify(
            service => service.EnsureLatestReleaseCachedAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        context.SessionSaver.Verify(saver => saver.SaveConfig(), Times.Never);
    }

    [Fact]
    public void TryStartOnlineRefreshAfterTerminalLaunch_WhenSharedLaunchStartsCachedDeployAndWeeklyOnlineRefresh()
    {
        var context = new TestContext(_tempDirectory.Path);
        using var cachedDeployStarted = new ManualResetEventSlim();
        using var onlineRefreshStarted = new ManualResetEventSlim();
        context.DeploymentService
            .Setup(service => service.TryDeployLatestCachedZipIfNewerThanSharedAsync(It.IsAny<CancellationToken>()))
            .Callback(cachedDeployStarted.Set)
            .ReturnsAsync(false);
        context.DeploymentService
            .Setup(service => service.EnsureLatestReleaseCachedAsync(It.IsAny<CancellationToken>()))
            .Callback(onlineRefreshStarted.Set)
            .Returns(Task.CompletedTask);

        context.Service.TryStartOnlineRefreshAfterTerminalLaunch(new AccountLaunchIdentity(TestSid));

        Assert.True(cachedDeployStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(onlineRefreshStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotNull(context.Database.Settings.LastWindowsTerminalLaunchRefreshAttemptUtc);
        context.SessionSaver.Verify(saver => saver.SaveConfig(), Times.Once);
        context.DeploymentService.Verify(
            service => service.TryDeployLatestCachedZipIfNewerThanSharedAsync(It.IsAny<CancellationToken>()),
            Times.Once);
        context.DeploymentService.Verify(
            service => service.EnsureLatestReleaseCachedAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void TryStartOnlineRefreshAfterTerminalLaunch_WhenLastAttemptIsRecent_OnlyDeploysCachedZip()
    {
        var context = new TestContext(_tempDirectory.Path);
        context.Database.Settings.LastWindowsTerminalLaunchRefreshAttemptUtc = DateTime.UtcNow.AddDays(-6);
        using var cachedDeployStarted = new ManualResetEventSlim();
        context.DeploymentService
            .Setup(service => service.TryDeployLatestCachedZipIfNewerThanSharedAsync(It.IsAny<CancellationToken>()))
            .Callback(cachedDeployStarted.Set)
            .ReturnsAsync(false);

        context.Service.TryStartOnlineRefreshAfterTerminalLaunch(new AccountLaunchIdentity(TestSid));

        Assert.True(cachedDeployStarted.Wait(TimeSpan.FromSeconds(5)));
        context.SessionSaver.Verify(saver => saver.SaveConfig(), Times.Never);
        context.DeploymentService.Verify(
            service => service.TryDeployLatestCachedZipIfNewerThanSharedAsync(It.IsAny<CancellationToken>()),
            Times.Once);
        context.DeploymentService.Verify(
            service => service.EnsureLatestReleaseCachedAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void TryStartOnlineRefreshAfterTerminalLaunch_WhenAppLocksAfterCachedDeploy_DoesNotSaveRefreshAttemptOrStartOnlineRefresh()
    {
        var context = new TestContext(_tempDirectory.Path);
        using var cachedDeployStarted = new ManualResetEventSlim();
        var isLocked = false;
        context.AppLock.Setup(control => control.IsLocked).Returns(() => isLocked);
        context.DeploymentService
            .Setup(service => service.TryDeployLatestCachedZipIfNewerThanSharedAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                isLocked = true;
                cachedDeployStarted.Set();
            })
            .ReturnsAsync(false);

        context.Service.TryStartOnlineRefreshAfterTerminalLaunch(new AccountLaunchIdentity(TestSid));

        Assert.True(cachedDeployStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.Null(context.Database.Settings.LastWindowsTerminalLaunchRefreshAttemptUtc);
        context.SessionSaver.Verify(saver => saver.SaveConfig(), Times.Never);
        context.DeploymentService.Verify(
            service => service.TryDeployLatestCachedZipIfNewerThanSharedAsync(It.IsAny<CancellationToken>()),
            Times.Once);
        context.DeploymentService.Verify(
            service => service.EnsureLatestReleaseCachedAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public void TryStartOnlineRefreshAfterTerminalLaunch_WhenTimestampSaveFails_StillDeploysCachedZip()
    {
        var context = new TestContext(_tempDirectory.Path);
        using var saveAttempted = new ManualResetEventSlim();
        context.SessionSaver
            .Setup(saver => saver.SaveConfig())
            .Callback(saveAttempted.Set)
            .Throws(new InvalidOperationException("save failed"));
        using var cachedDeployStarted = new ManualResetEventSlim();
        context.DeploymentService
            .Setup(service => service.TryDeployLatestCachedZipIfNewerThanSharedAsync(It.IsAny<CancellationToken>()))
            .Callback(cachedDeployStarted.Set)
            .ReturnsAsync(false);

        context.Service.TryStartOnlineRefreshAfterTerminalLaunch(new AccountLaunchIdentity(TestSid));

        Assert.True(cachedDeployStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(saveAttempted.Wait(TimeSpan.FromSeconds(5)));
        Assert.NotNull(context.Database.Settings.LastWindowsTerminalLaunchRefreshAttemptUtc);
        context.DeploymentService.Verify(
            service => service.TryDeployLatestCachedZipIfNewerThanSharedAsync(It.IsAny<CancellationToken>()),
            Times.Once);
        context.DeploymentService.Verify(
            service => service.EnsureLatestReleaseCachedAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    public void Dispose()
    {
        _tempDirectory.Dispose();
    }

    private sealed class TestContext
    {
        public TestContext(string programDataRootPath)
        {
            DeploymentPaths = new WindowsTerminalDeploymentPaths(new TestProgramDataKnownPathResolver(programDataRootPath));
            DatabaseProvider.Setup(provider => provider.GetDatabase()).Returns(Database);
            AccountState.Setup(service => service.ResolveLaunchTarget(TestSid)).Returns(DeploymentPaths.SharedExecutablePath);
            AccountState.Setup(service => service.ResolveLaunchTarget(It.IsAny<AccountLaunchIdentity>())).Returns(DeploymentPaths.SharedExecutablePath);
            DeploymentService
                .Setup(service => service.TryDeployLatestCachedZipIfNewerThanSharedAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            DeploymentService
                .Setup(service => service.EnsureLatestReleaseCachedAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            DeploymentService
                .Setup(service => service.EnsureSharedDeploymentReadyAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            ProgressRunner
                .Setup(runner => runner.RunAsync(
                    It.IsAny<string>(),
                    It.IsAny<Func<CancellationToken, Task>>()))
                .Returns<string, Func<CancellationToken, Task>>((_, operation) => operation(CancellationToken.None));
            Service = new WindowsTerminalLaunchRefreshService(
                AppLock.Object,
                DatabaseProvider.Object,
                SessionSaver.Object,
                AccountState.Object,
                DeploymentService.Object,
                ProgressRunner.Object,
                DeploymentPaths,
                Mock.Of<ILoggingService>());
        }

        public AppDatabase Database { get; } = new();
        public WindowsTerminalDeploymentPaths DeploymentPaths { get; }
        public Mock<IAppLockControl> AppLock { get; } = new();
        public Mock<IDatabaseProvider> DatabaseProvider { get; } = new();
        public Mock<ISessionSaver> SessionSaver { get; } = new();
        public Mock<IWindowsTerminalAccountStateService> AccountState { get; } = new();
        public Mock<IWindowsTerminalDeploymentService> DeploymentService { get; } = new();
        public Mock<IWindowsTerminalDeploymentProgressRunner> ProgressRunner { get; } = new();
        public WindowsTerminalLaunchRefreshService Service { get; }
    }
}
