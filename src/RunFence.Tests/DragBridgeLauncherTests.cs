using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.DragBridge;
using RunFence.Launch;
using RunFence.Launch.Tokens;
using Xunit;

namespace RunFence.Tests;

public class DragBridgeLauncherTests
{
    private readonly Mock<ILaunchFacade> _facade;
    private readonly Mock<ILoggingService> _log;
    private readonly DragBridgeLauncher _launcher;

    public DragBridgeLauncherTests()
    {
        _facade = new Mock<ILaunchFacade>();
        _log = new Mock<ILoggingService>();
        _launcher = new DragBridgeLauncher(_facade.Object, _log.Object);
    }

    // --- LaunchDirect: failure handling ---

    [Fact]
    public void LaunchDirect_FacadeThrows_ReturnsNullAndLogsError()
    {
        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Throws(new InvalidOperationException("launch failed"));

        var result = _launcher.LaunchDirect(@"C:\nonexistent_dragbridge_xyz.exe", ["--copy"]);

        Assert.Null(result);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    // --- LaunchDirect: argument forwarding ---

    [Fact]
    public void LaunchDirect_PassesExePathToFacade()
    {
        // Verify the exe path is forwarded to the launch target
        ProcessLaunchTarget? capturedTarget = null;
        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Callback<ProcessLaunchTarget, LaunchIdentity, Func<string, string, bool>?>((t, _, _) => capturedTarget = t)
            .Returns((ProcessInfo?)null);

        _launcher.LaunchDirect(@"C:\tools\bridge.exe", ["--copy", "--source", "file.txt"]);

        Assert.NotNull(capturedTarget);
        Assert.Equal(@"C:\tools\bridge.exe", capturedTarget!.ExePath);
        Assert.True(capturedTarget.HideWindow);
    }

    [Fact]
    public void LaunchDirect_PassesArgumentsToFacade()
    {
        // Verify the argument list is combined and forwarded
        ProcessLaunchTarget? capturedTarget = null;
        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Callback<ProcessLaunchTarget, LaunchIdentity, Func<string, string, bool>?>((t, _, _) => capturedTarget = t)
            .Returns((ProcessInfo?)null);

        _launcher.LaunchDirect(@"C:\tools\bridge.exe", ["--copy", "source.txt"]);

        Assert.NotNull(capturedTarget);
        // Arguments are combined from list
        Assert.Contains("--copy", capturedTarget!.Arguments);
        Assert.Contains("source.txt", capturedTarget.Arguments);
    }

    [Theory]
    [InlineData(PrivilegeLevel.HighestAllowed)]
    [InlineData(PrivilegeLevel.Basic)]
    [InlineData(PrivilegeLevel.LowIntegrity)]
    public void LaunchDirect_PrivilegeLevel_ForwardedToIdentity(PrivilegeLevel mode)
    {
        // Verify the privilege level is set on the launch identity
        LaunchIdentity? capturedIdentity = null;
        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Callback<ProcessLaunchTarget, LaunchIdentity, Func<string, string, bool>?>((_, id, _) => capturedIdentity = id)
            .Returns((ProcessInfo?)null);

        _launcher.LaunchDirect(@"C:\tools\bridge.exe", ["--copy"], mode);

        Assert.NotNull(capturedIdentity);
        Assert.IsType<AccountLaunchIdentity>(capturedIdentity);
        Assert.Equal(mode, ((AccountLaunchIdentity)capturedIdentity!).PrivilegeLevel);
    }

    // --- LaunchManaged: failure handling ---

    [Fact]
    public void LaunchManaged_FacadeReturnsNull_ThrowsInvalidOperation()
    {
        // LaunchManaged must throw when the facade returns null — the caller relies on a
        // non-null process handle to track/wait for the bridge process
        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Returns((ProcessInfo?)null);

        Assert.Throws<InvalidOperationException>(() =>
            _launcher.LaunchManaged(@"C:\tools\bridge.exe", "S-1-5-21-9999-9999-9999-1001", []));
    }

    [Fact]
    public void LaunchManaged_PassesSidToFacade()
    {
        // Verify the account SID is forwarded as AccountLaunchIdentity to the facade.
        // The facade returns a non-null ProcessInfo so the call completes without exception,
        // keeping this test focused on identity forwarding rather than exception-path behavior.
        const string targetSid = "S-1-5-21-9999-9999-9999-1001";
        LaunchIdentity? capturedIdentity = null;
        var processInfo = new ProcessInfo(new ProcessLaunchNative.PROCESS_INFORMATION());
        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Callback<ProcessLaunchTarget, LaunchIdentity, Func<string, string, bool>?>((_, id, _) => capturedIdentity = id)
            .Returns(processInfo);

        using var result = _launcher.LaunchManaged(@"C:\tools\bridge.exe", targetSid, []);

        Assert.NotNull(result);
        Assert.IsType<AccountLaunchIdentity>(capturedIdentity);
        Assert.Equal(targetSid, ((AccountLaunchIdentity)capturedIdentity!).Sid);
    }

    // --- LaunchDeElevated: failure handling ---

    [Fact]
    public void LaunchDeElevated_NoInteractiveSid_ReturnsNullAndLogsError()
    {
        // GetInteractiveUserSid() returns null in tests (no explorer.exe session),
        // causing AccountLaunchIdentity.InteractiveUser to throw, which is caught and logged.
        var result = _launcher.LaunchDeElevated(@"C:\tools\dragbridge.exe", ["--paste"]);

        Assert.Null(result);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void LaunchDeElevated_FacadeThrows_ReturnsNullAndLogsError()
    {
        // If the interactive SID is available but facade throws, error is logged and null returned.
        // This test only exercises the facade-throws branch on machines where explorer is running.
        var interactiveSid = SidResolutionHelper.GetInteractiveUserSid();
        // xunit v2 dynamic skip (SkipException) shows as Fail in CLI; use early return instead.
        // This branch is only reached on machines where explorer.exe runs under the test user.
        if (interactiveSid == null)
            return;

        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Throws(new InvalidOperationException("de-elevated launch failed"));

        var result = _launcher.LaunchDeElevated(@"C:\tools\dragbridge.exe", ["--paste"]);

        Assert.Null(result);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }
}
