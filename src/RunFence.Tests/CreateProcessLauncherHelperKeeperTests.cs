using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Tokens;
using RunFence.Launching.Resolution;
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
            new Mock<IProcessJobManager>().Object,
            jobKeeperService.Object,
            restrictedCoordinator.Object,
            new Mock<IExecutablePathResolver>().Object);
        var target = new ProcessLaunchTarget(@"C:\Apps\App.exe");
        var expected = new ProcessInfo(new ProcessLaunchNative.PROCESS_INFORMATION
        {
            hProcess = IntPtr.Zero,
            dwProcessId = 4321,
        });
        var identity = new AccountLaunchIdentity(Sid)
        {
            Credentials = LaunchCredentials.CurrentAccount,
            PrivilegeLevel = PrivilegeLevel.Basic,
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
}
