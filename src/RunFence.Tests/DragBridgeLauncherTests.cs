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
    public void LaunchDirect_FacadeThrows_ReturnsNull()
    {
        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), It.IsAny<Func<string, string, bool>?>()))
            .Throws(new InvalidOperationException("launch failed"));

        var result = _launcher.LaunchDirect(@"C:\nonexistent_dragbridge_xyz.exe", ["--copy"]);

        Assert.Null(result);
    }

    // --- LaunchDirect: argument forwarding ---

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
    public void LaunchManaged_ForwardsSidPrivilegeAndArguments()
    {
        ProcessLaunchTarget? capturedTarget = null;
        LaunchIdentity? capturedIdentity = null;
        var args = new List<string> { "--copy", @"C:\files\source name.txt", "--force" };
        var expectedArguments = ProcessLaunchTarget.CombineArguments(args.ToList());
        var processInfo = TestProcessInfoFactory.Native(new ProcessLaunchNative.PROCESS_INFORMATION());

        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Callback<ProcessLaunchTarget, LaunchIdentity, Func<string, string, bool>?>((target, identity, _) =>
            {
                capturedTarget = target;
                capturedIdentity = identity;
            })
            .Returns(MakeLaunchResult(processInfo));

        using var result = _launcher.LaunchManaged(
            @"C:\tools\bridge.exe",
            "S-1-5-21-9999-9999-9999-1001",
            args,
            PrivilegeLevel.Basic);

        Assert.Same(processInfo, result);
        var identity = Assert.IsType<AccountLaunchIdentity>(capturedIdentity);
        Assert.Equal("S-1-5-21-9999-9999-9999-1001", identity.Sid);
        Assert.Equal(PrivilegeLevel.Basic, identity.PrivilegeLevel);
        Assert.NotNull(capturedTarget);
        Assert.Equal(@"C:\tools\bridge.exe", capturedTarget!.ExePath);
        Assert.Equal(expectedArguments, capturedTarget.Arguments);
        Assert.True(capturedTarget.SuppressStartupFeedback);
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
        var processInfo = TestProcessInfoFactory.Native(new ProcessLaunchNative.PROCESS_INFORMATION());
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
    public void LaunchDeElevated_NoInteractiveSid_ReturnsNull()
    {
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);

        var result = _launcher.LaunchDeElevated(@"C:\tools\dragbridge.exe", ["--paste"]);

        Assert.Null(result);
    }

    [Fact]
    public void LaunchDeElevated_FacadeThrows_ReturnsNull()
    {
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns("S-1-5-21-9-9-9-1001");

        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Throws(new InvalidOperationException("de-elevated launch failed"));

        var result = _launcher.LaunchDeElevated(@"C:\tools\dragbridge.exe", ["--paste"]);

        Assert.Null(result);
    }

    [Fact]
    public void LaunchDeElevated_ForwardsInteractiveSidPrivilegeAndArguments()
    {
        ProcessLaunchTarget? capturedTarget = null;
        LaunchIdentity? capturedIdentity = null;
        var args = new List<string> { "--paste", @"C:\dest\out file.txt", "--mode", "copy" };
        var expectedArguments = ProcessLaunchTarget.CombineArguments(args.ToList());
        var processInfo = TestProcessInfoFactory.Native(new ProcessLaunchNative.PROCESS_INFORMATION());
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns("S-1-5-21-222-333-444-1005");
        _facade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Callback<ProcessLaunchTarget, LaunchIdentity, Func<string, string, bool>?>((target, identity, _) =>
            {
                capturedTarget = target;
                capturedIdentity = identity;
            })
            .Returns(MakeLaunchResult(processInfo));

        using var result = _launcher.LaunchDeElevated(@"C:\tools\dragbridge.exe", args, PrivilegeLevel.Isolated);

        Assert.Same(processInfo, result);
        var identity = Assert.IsType<AccountLaunchIdentity>(capturedIdentity);
        Assert.Equal("S-1-5-21-222-333-444-1005", identity.Sid);
        Assert.Equal(PrivilegeLevel.Isolated, identity.PrivilegeLevel);
        Assert.NotNull(capturedTarget);
        Assert.Equal(@"C:\tools\dragbridge.exe", capturedTarget!.ExePath);
        Assert.Equal(expectedArguments, capturedTarget.Arguments);
        Assert.True(capturedTarget.SuppressStartupFeedback);
    }

}
