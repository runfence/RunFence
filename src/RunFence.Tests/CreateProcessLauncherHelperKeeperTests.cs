using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Tokens;
using Xunit;

namespace RunFence.Tests;

public sealed class CreateProcessLauncherHelperKeeperTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";

    [Fact]
    public void LaunchUsingAcquiredToken_ExistingKeeperFastPath_BypassesTokenPreparation()
    {
        var jobKeeperService = new Mock<IJobKeeperService>();
        var restrictedCoordinator = new Mock<IRestrictedJobLaunchCoordinator>();
        var helper = new CreateProcessLauncherHelper(
            new Mock<ILoggingService>().Object,
            new Mock<IElevatedLinkedTokenProvider>().Object,
            new Mock<ISaferDeElevationHelper>().Object,
            new Mock<ITokenIntegrityLevelService>().Object,
            new Mock<IProcessJobManager>().Object,
            jobKeeperService.Object,
            restrictedCoordinator.Object,
            new Mock<IPreparedTokenProcessLauncher>().Object,
            new InlineProfileKeeperBootstrapContext(),
            profileKeeperExePath: @"C:\RunFence\RunFence.ProfileKeeper.exe");
        var target = new ProcessLaunchTarget(@"C:\Apps\App.exe");
        var expected = new ProcessInfo(new ProcessLaunchNative.PROCESS_INFORMATION
        {
            hProcess = IntPtr.Zero,
            dwProcessId = 4321,
        });
        var identity = new AccountLaunchIdentity(Sid)
        {
            Credentials = LaunchCredentials.CurrentAccount,
            PrivilegeLevel = PrivilegeLevel.Isolated,
        };

        jobKeeperService.Setup(s => s.HasJobKeeper(Sid, false)).Returns(true);
        restrictedCoordinator.Setup(c => c.LaunchViaJobKeeper(Sid, false, target)).Returns(expected);

        var result = helper.LaunchUsingAcquiredToken(IntPtr.Zero, target, identity);

        Assert.Same(expected, result);
        restrictedCoordinator.Verify(c => c.LaunchViaJobKeeper(Sid, false, target), Times.Once);
        restrictedCoordinator.Verify(c => c.SeedJobKeeperAndLaunch(
            It.IsAny<IntPtr>(),
            It.IsAny<LaunchTokenSource>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<ProcessLaunchTarget>()), Times.Never);
    }

    [Fact]
    public void LaunchUsingAcquiredToken_StaleKeeperFastPathFailure_FallsBackToReseedInSameAttempt()
    {
        var jobKeeperService = new Mock<IJobKeeperService>();
        var restrictedCoordinator = new Mock<IRestrictedJobLaunchCoordinator>();
        var saferDeElevationHelper = new Mock<ISaferDeElevationHelper>();
        var helper = new CreateProcessLauncherHelper(
            new Mock<ILoggingService>().Object,
            new Mock<IElevatedLinkedTokenProvider>().Object,
            saferDeElevationHelper.Object,
            new Mock<ITokenIntegrityLevelService>().Object,
            new Mock<IProcessJobManager>().Object,
            jobKeeperService.Object,
            restrictedCoordinator.Object,
            new Mock<IPreparedTokenProcessLauncher>().Object,
            new InlineProfileKeeperBootstrapContext(),
            profileKeeperExePath: @"C:\RunFence\RunFence.ProfileKeeper.exe");
        var target = new ProcessLaunchTarget(@"C:\Apps\App.exe");
        var identity = new AccountLaunchIdentity(Sid)
        {
            Credentials = LaunchCredentials.CurrentAccount,
            PrivilegeLevel = PrivilegeLevel.Isolated,
        };
        var currentToken = OpenCurrentProcessToken();

        try
        {
            jobKeeperService.Setup(s => s.HasJobKeeper(Sid, false)).Returns(true);
            restrictedCoordinator.Setup(c => c.LaunchViaJobKeeper(Sid, false, target))
                .Throws(new StaleJobKeeperException(Sid));
            restrictedCoordinator.Setup(c => c.SeedJobKeeperAndLaunch(
                    It.IsAny<IntPtr>(),
                    LaunchTokenSource.CurrentProcess,
                    Sid,
                    false,
                    target))
                .Returns(new ProcessLaunchNative.PROCESS_INFORMATION
                {
                    hProcess = IntPtr.Zero,
                    dwProcessId = 5678,
                });

            var result = helper.LaunchUsingAcquiredToken(currentToken, target, identity);

            Assert.Equal(5678, result.Id);
            restrictedCoordinator.Verify(c => c.LaunchViaJobKeeper(Sid, false, target), Times.Once);
            restrictedCoordinator.Verify(c => c.SeedJobKeeperAndLaunch(
                It.IsAny<IntPtr>(),
                LaunchTokenSource.CurrentProcess,
                Sid,
                false,
                target), Times.Once);
            saferDeElevationHelper.Verify(s => s.CreateDeElevatedToken(It.IsAny<IntPtr>()), Times.Never);
        }
        finally
        {
            ProcessNative.CloseHandle(currentToken);
        }
    }

    [Fact]
    public void LaunchUsingAcquiredToken_NonStaleKeeperInvalidOperation_DoesNotFallback()
    {
        var jobKeeperService = new Mock<IJobKeeperService>();
        var restrictedCoordinator = new Mock<IRestrictedJobLaunchCoordinator>();
        var helper = new CreateProcessLauncherHelper(
            new Mock<ILoggingService>().Object,
            new Mock<IElevatedLinkedTokenProvider>().Object,
            new Mock<ISaferDeElevationHelper>().Object,
            new Mock<ITokenIntegrityLevelService>().Object,
            new Mock<IProcessJobManager>().Object,
            jobKeeperService.Object,
            restrictedCoordinator.Object,
            new Mock<IPreparedTokenProcessLauncher>().Object,
            new InlineProfileKeeperBootstrapContext(),
            profileKeeperExePath: @"C:\RunFence\RunFence.ProfileKeeper.exe");
        var target = new ProcessLaunchTarget(@"C:\Apps\App.exe");
        var identity = new AccountLaunchIdentity(Sid)
        {
            Credentials = LaunchCredentials.CurrentAccount,
            PrivilegeLevel = PrivilegeLevel.Isolated,
        };

        jobKeeperService.Setup(s => s.HasJobKeeper(Sid, false)).Returns(true);
        restrictedCoordinator.Setup(c => c.LaunchViaJobKeeper(Sid, false, target))
            .Throws(new InvalidOperationException("OpenLaunchedProcess failed."));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            helper.LaunchUsingAcquiredToken(IntPtr.Zero, target, identity));

        Assert.Equal("OpenLaunchedProcess failed.", ex.Message);
        restrictedCoordinator.Verify(c => c.SeedJobKeeperAndLaunch(
            It.IsAny<IntPtr>(),
            It.IsAny<LaunchTokenSource>(),
            It.IsAny<string>(),
            It.IsAny<bool>(),
            It.IsAny<ProcessLaunchTarget>()), Times.Never);
    }

    private static IntPtr OpenCurrentProcessToken()
    {
        if (!ProcessNative.OpenProcessToken(
                System.Diagnostics.Process.GetCurrentProcess().Handle,
                ProcessLaunchNative.TOKEN_DUPLICATE | ProcessLaunchNative.TOKEN_QUERY,
                out var token))
        {
            throw new InvalidOperationException("Unable to open current process token for test setup.");
        }

        return token;
    }

    private sealed class InlineProfileKeeperBootstrapContext : IProfileKeeperBootstrapContext
    {
        public T Run<T>(Func<T> action) => action();
    }
}
