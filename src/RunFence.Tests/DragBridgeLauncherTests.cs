using System.Security.Principal;
using Moq;
using RunFence.Core;
using RunFence.Core.Models;
using RunFence.DragBridge;
using RunFence.Infrastructure;
using RunFence.Launch;
using RunFence.Launch.Tokens;
using Xunit;

namespace RunFence.Tests;

public class DragBridgeLauncherTests
{
    private readonly Mock<ILaunchFacade> _facade;
    private readonly Mock<IInteractiveUserSidResolver> _interactiveUserSidResolver;
    private readonly Mock<ILoggingService> _log;
    private readonly DragBridgeLauncher _launcher;

    public DragBridgeLauncherTests()
    {
        _facade = new Mock<ILaunchFacade>();
        _interactiveUserSidResolver = new Mock<IInteractiveUserSidResolver>();
        _log = new Mock<ILoggingService>();
        _launcher = new DragBridgeLauncher(_facade.Object, _interactiveUserSidResolver.Object, _log.Object);
    }

    private static LaunchExecutionResult MakeLaunchResult(ProcessInfo? process = null, params string[] warnings)
        => new(
            warnings.Length == 0 ? LaunchExecutionStatus.ProcessStarted : LaunchExecutionStatus.ProcessStartedWithMaintenanceWarnings,
            process,
            warnings);

    // --- Pipe mandatory label ---

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void CreatePipeServer_LowIntegrityFlag_ControlsMandatoryLabel(bool allowLowIntegrityClient, int expectedCalls)
    {
        var mandatoryLabel = new Mock<IKernelObjectMandatoryLabelService>();
        var processLauncher = new DragBridgeProcessLauncher(
            _launcher,
            _log.Object,
            new LambdaUiThreadInvoker(a => a(), a => a()),
            mandatoryLabel.Object,
            @"C:\tools\dragbridge.exe");

        using var pipe = processLauncher.CreatePipeServer(
            $"RunFenceTest_DragBridgePipe_{Guid.NewGuid():N}",
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            null,
            allowLowIntegrityClient);

        mandatoryLabel.Verify(s => s.ApplyLowIntegrityLabel(It.IsAny<IntPtr>()), Times.Exactly(expectedCalls));
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
            .Returns(MakeLaunchResult());

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
            .Returns(MakeLaunchResult());

        _launcher.LaunchDirect(@"C:\tools\bridge.exe", ["--copy", "source.txt"]);

        Assert.NotNull(capturedTarget);
        // Arguments are combined from list
        Assert.Contains("--copy", capturedTarget!.Arguments);
        Assert.Contains("source.txt", capturedTarget.Arguments);
    }

    [Fact]
    public void LaunchDirect_MaterializesArgumentsExactly()
    {
        ProcessLaunchTarget? capturedTarget = null;
        var args = new List<string> { "--copy", @"C:\source\path with spaces.txt", "--force", "true" };
        var expectedArguments = ProcessLaunchTarget.CombineArguments(args.ToList());

        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Callback<ProcessLaunchTarget, LaunchIdentity, Func<string, string, bool>?>((t, _, _) => capturedTarget = t)
            .Returns(MakeLaunchResult());

        _launcher.LaunchDirect(@"C:\tools\bridge.exe", args);

        Assert.NotNull(capturedTarget);
        Assert.Equal(expectedArguments, capturedTarget!.Arguments);
    }

    [Theory]
    [InlineData(PrivilegeLevel.HighestAllowed)]
    [InlineData(PrivilegeLevel.Isolated)]
    [InlineData(PrivilegeLevel.LowIntegrity)]
    public void LaunchDirect_PrivilegeLevel_ForwardedToIdentity(PrivilegeLevel mode)
    {
        // Verify the privilege level is set on the launch identity
        LaunchIdentity? capturedIdentity = null;
        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Callback<ProcessLaunchTarget, LaunchIdentity, Func<string, string, bool>?>((_, id, _) => capturedIdentity = id)
            .Returns(MakeLaunchResult());

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
            .Returns(MakeLaunchResult());

        Assert.Throws<InvalidOperationException>(() =>
            _launcher.LaunchManaged(@"C:\tools\bridge.exe", "S-1-5-21-9999-9999-9999-1001", [],
                PrivilegeLevel.Isolated));
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
            .Returns(MakeLaunchResult(processInfo));

        using var result = _launcher.LaunchManaged(@"C:\tools\bridge.exe", targetSid, [],
            PrivilegeLevel.Basic);

        Assert.NotNull(result);
        Assert.IsType<AccountLaunchIdentity>(capturedIdentity);
        Assert.Equal(targetSid, ((AccountLaunchIdentity)capturedIdentity!).Sid);
        Assert.Equal(PrivilegeLevel.Basic, ((AccountLaunchIdentity)capturedIdentity).PrivilegeLevel);
    }

    [Fact]
    public void LaunchManaged_MaintenanceWarning_ReturnsProcessAndLogsWarning()
    {
        var processInfo = new ProcessInfo(new ProcessLaunchNative.PROCESS_INFORMATION());
        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Returns(MakeLaunchResult(processInfo, "post-launch hook failed"));

        using var result = _launcher.LaunchManaged(@"C:\tools\bridge.exe", "S-1-5-21-9999-9999-9999-1001", [],
            PrivilegeLevel.Isolated);

        Assert.NotNull(result);
        _log.Verify(l => l.Warn(It.Is<string>(s => s.Contains("post-launch hook failed", StringComparison.Ordinal))), Times.Once);
    }

    [Fact]
    public void LaunchAppContainer_UsesAppContainerIdentity()
    {
        var entry = new AppContainerEntry
        {
            Name = "sandbox",
            DisplayName = "Sandbox",
            Sid = "S-1-15-2-42"
        };
        LaunchIdentity? capturedIdentity = null;
        var processInfo = new ProcessInfo(new ProcessLaunchNative.PROCESS_INFORMATION());
        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Callback<ProcessLaunchTarget, LaunchIdentity, Func<string, string, bool>?>((_, identity, _) => capturedIdentity = identity)
            .Returns(MakeLaunchResult(processInfo));

        using var result = _launcher.LaunchAppContainer(@"C:\tools\bridge.exe", entry, []);

        Assert.NotNull(result);
        var identity = Assert.IsType<AppContainerLaunchIdentity>(capturedIdentity);
        Assert.Same(entry, identity.Entry);
    }

    // --- LaunchDeElevated: failure handling ---

    [Fact]
    public void LaunchDeElevated_NoInteractiveSid_ReturnsNullAndLogsError()
    {
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);

        var result = _launcher.LaunchDeElevated(@"C:\tools\dragbridge.exe", ["--paste"]);

        Assert.Null(result);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void LaunchDeElevated_FacadeThrows_ReturnsNullAndLogsError()
    {
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns("S-1-5-21-9-9-9-1001");

        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Throws(new InvalidOperationException("de-elevated launch failed"));

        var result = _launcher.LaunchDeElevated(@"C:\tools\dragbridge.exe", ["--paste"]);

        Assert.Null(result);
        _log.Verify(l => l.Error(It.IsAny<string>(), It.IsAny<Exception>()), Times.Once);
    }

    [Fact]
    public void LaunchDeElevated_UsesResolvedInteractiveUserSid()
    {
        const string interactiveSid = "S-1-5-21-9-9-9-1001";
        LaunchIdentity? capturedIdentity = null;
        var processInfo = new ProcessInfo(new ProcessLaunchNative.PROCESS_INFORMATION());
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns(interactiveSid);
        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Callback<ProcessLaunchTarget, LaunchIdentity, Func<string, string, bool>?>((_, id, _) => capturedIdentity = id)
            .Returns(MakeLaunchResult(processInfo));

        using var result = _launcher.LaunchDeElevated(@"C:\tools\dragbridge.exe", ["--paste"], PrivilegeLevel.Isolated);

        Assert.NotNull(result);
        Assert.IsType<AccountLaunchIdentity>(capturedIdentity);
        Assert.Equal(interactiveSid, ((AccountLaunchIdentity)capturedIdentity!).Sid);
        Assert.Equal(PrivilegeLevel.Isolated, ((AccountLaunchIdentity)capturedIdentity).PrivilegeLevel);
    }
}
