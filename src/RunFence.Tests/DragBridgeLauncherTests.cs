using System.ComponentModel;
using System.Diagnostics;
using Moq;
using RunFence.Core;
using RunFence.DragBridge;
using RunFence.Launch;
using RunFence.Launch.Tokens;
using Xunit;

namespace RunFence.Tests;

public class DragBridgeLauncherTests
{
    private readonly Mock<ISplitTokenLauncher> _splitToken;
    private readonly Mock<ICurrentAccountLauncher> _currentAccount;
    private readonly Mock<ILoggingService> _log;
    private readonly DragBridgeLauncher _launcher;

    public DragBridgeLauncherTests()
    {
        var orchestrator = new Mock<IAppLaunchOrchestrator>();
        _splitToken = new Mock<ISplitTokenLauncher>();
        _currentAccount = new Mock<ICurrentAccountLauncher>();
        _log = new Mock<ILoggingService>();
        _launcher = new DragBridgeLauncher(orchestrator.Object, _splitToken.Object, _currentAccount.Object, _log.Object);
    }

    [Fact]
    public void LaunchDirect_NoSplitToken_LaunchFailure_ReturnsNullAndLogsError()
    {
        _currentAccount.Setup(c => c.Launch(It.IsAny<ProcessStartInfo>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<bool>()))
            .Throws(new Win32Exception(2));

        var result = _launcher.LaunchDirect(@"C:\nonexistent_dragbridge_xyz.exe", ["--copy"]);

        Assert.Null(result);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void LaunchDirect_SplitTokenMediumIL_NonExistentExe_ReturnsNullAndLogsError()
    {
        _splitToken.Setup(s => s.Launch(It.IsAny<ProcessStartInfo>(), null, null, null, false, It.IsAny<LaunchTokenSource>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<bool>()))
            .Throws(new Win32Exception(2));

        var result = _launcher.LaunchDirect(@"C:\nonexistent_dragbridge_xyz.exe", ["--copy"],
            useSplitToken: true, useLowIntegrity: false);

        Assert.Null(result);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void LaunchDirect_SplitTokenLowIL_NonExistentExe_ReturnsNullAndLogsError()
    {
        _splitToken.Setup(s => s.Launch(It.IsAny<ProcessStartInfo>(), null, null, null, true, It.IsAny<LaunchTokenSource>(), It.IsAny<Dictionary<string, string>?>(), It.IsAny<bool>()))
            .Throws(new Win32Exception(2));

        var result = _launcher.LaunchDirect(@"C:\nonexistent_dragbridge_xyz.exe", ["--copy"],
            useSplitToken: true, useLowIntegrity: true);

        Assert.Null(result);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void LaunchDeElevated_NoInteractiveSid_ReturnsZeroAndLogsError()
    {
        // GetInteractiveUserSid() returns null in tests (no explorer.exe session)
        var pid = _launcher.LaunchDeElevated(@"C:\tools\dragbridge.exe", ["--paste"]);

        Assert.Equal(0, pid);
        _log.Verify(l => l.Error(It.IsAny<string>()), Times.Once);
    }
}
