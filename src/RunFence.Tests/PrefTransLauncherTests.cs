using System.ComponentModel;
using Moq;
using RunFence.Core;
using RunFence.Launch;
using RunFence.Launch.Tokens;
using RunFence.PrefTrans;
using Xunit;

namespace RunFence.Tests;

public class PrefTransLauncherTests
{
    private readonly Mock<ILaunchFacade> _launchFacade = new(MockBehavior.Strict);
    private readonly Mock<ILoggingService> _log = new();
    private readonly Mock<IPrefTransLogWorkspace> _workspace = new(MockBehavior.Strict);
    private readonly Mock<IPrefTransProcessWaiter> _processWaiter = new(MockBehavior.Strict);

    [Fact]
    public void RunAndWait_WhenLogWorkspaceCreationFails_ReturnsGenericFailureWithoutLaunching()
    {
        _workspace.Setup(w => w.CreateLogFile("S-1-5-21-test"))
            .Returns(new PrefTransLogWorkspaceResult(false, null, "workspace failed"));
        var launcher = CreateLauncher();

        var result = launcher.RunAndWait("preftrans.exe", "store", "settings.json", "S-1-5-21-test", 1234, null);

        Assert.False(result.Success);
        Assert.Equal("Secure log workspace verification failed. Transfer aborted.", result.Message);
        _launchFacade.VerifyNoOtherCalls();
    }

    [Fact]
    public void RunAndWait_WhenLaunchFailsWithCredentialError_DeletesLogFile()
    {
        const string accountSid = "S-1-5-21-test";
        const string logFilePath = @"C:\temp\preftrans.log";
        _workspace.Setup(w => w.CreateLogFile(accountSid))
            .Returns(new PrefTransLogWorkspaceResult(true, logFilePath, null));
        _launchFacade.Setup(f => f.LaunchFile(
                It.IsAny<ProcessLaunchTarget>(),
                It.Is<LaunchIdentity>(identity => identity.Sid == accountSid),
                null))
            .Throws(new Win32Exception(ProcessLaunchNative.Win32ErrorLogonFailure, "bad password"));
        _workspace.Setup(w => w.TryDeleteLogFile(logFilePath));
        var launcher = CreateLauncher();

        var result = launcher.RunAndWait("preftrans.exe", "load", "settings.json", accountSid, 1234, null);

        Assert.False(result.Success);
        Assert.Equal("Stored credentials are incorrect.", result.Message);
        _workspace.Verify(w => w.TryDeleteLogFile(logFilePath), Times.Once);
    }

    [Fact]
    public void RunAndWait_WhenLaunchFailsWithUnexpectedError_DeletesLogFile()
    {
        const string accountSid = "S-1-5-21-test";
        const string logFilePath = @"C:\temp\preftrans.log";
        _workspace.Setup(w => w.CreateLogFile(accountSid))
            .Returns(new PrefTransLogWorkspaceResult(true, logFilePath, null));
        _launchFacade.Setup(f => f.LaunchFile(
                It.IsAny<ProcessLaunchTarget>(),
                It.Is<LaunchIdentity>(identity => identity.Sid == accountSid),
                null))
            .Throws(new InvalidOperationException("launch failed"));
        _workspace.Setup(w => w.TryDeleteLogFile(logFilePath));
        var launcher = CreateLauncher();

        var result = launcher.RunAndWait("preftrans.exe", "load", "settings.json", accountSid, 1234, null);

        Assert.False(result.Success);
        Assert.Equal("Operation failed: launch failed", result.Message);
        _workspace.Verify(w => w.TryDeleteLogFile(logFilePath), Times.Once);
    }

    [Fact]
    public void RunAndWait_WhenProcessSucceeds_DeletesLogFileAndPassesExpectedArguments()
    {
        const string accountSid = "S-1-5-21-test";
        const string prefTransPath = @"C:\tools\preftrans.exe";
        const string filePath = @"C:\temp\settings.json";
        const string logFilePath = @"C:\temp\preftrans.log";

        _workspace.Setup(w => w.CreateLogFile(accountSid))
            .Returns(new PrefTransLogWorkspaceResult(true, logFilePath, null));
        _launchFacade.Setup(f => f.LaunchFile(
                It.Is<ProcessLaunchTarget>(target =>
                target.ExePath == prefTransPath
                && target.HideWindow
                && target.SuppressStartupFeedback
                && target.Arguments == ProcessLaunchTarget.CombineArguments(new List<string> { "store", filePath, "--logfile", logFilePath })),
                It.Is<LaunchIdentity>(identity => identity.Sid == accountSid),
                null))
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, TestProcessInfoFactory.Empty()));
        _workspace.Setup(w => w.TryDeleteLogFile(logFilePath));
        _processWaiter.Setup(w => w.WaitForResult(
                It.IsAny<ProcessInfo>(),
                1234,
                logFilePath,
                null))
            .Returns(new SettingsTransferResult(true, "helper ok"));
        var launcher = CreateLauncher();

        var result = launcher.RunAndWait(prefTransPath, "store", filePath, accountSid, 1234, null);

        Assert.True(result.Success);
        _workspace.Verify(w => w.TryDeleteLogFile(logFilePath), Times.Once);
    }

    [Fact]
    public void RunAndWait_WhenLaunchReturnsMaintenanceWarnings_LogsWarningAndStillSucceeds()
    {
        const string accountSid = "S-1-5-21-test";
        const string logFilePath = @"C:\temp\preftrans.log";

        _workspace.Setup(w => w.CreateLogFile(accountSid))
            .Returns(new PrefTransLogWorkspaceResult(true, logFilePath, null));
        _launchFacade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Returns(new LaunchExecutionResult(
                LaunchExecutionStatus.ProcessStartedWithMaintenanceWarnings,
                TestProcessInfoFactory.Empty(),
                ["warning text"]));
        _workspace.Setup(w => w.TryDeleteLogFile(logFilePath));
        _processWaiter.Setup(w => w.WaitForResult(
                It.IsAny<ProcessInfo>(),
                1234,
                logFilePath,
                null))
            .Returns(new SettingsTransferResult(true, ""));
        var launcher = CreateLauncher();

        var result = launcher.RunAndWait("preftrans.exe", "store", "settings.json", accountSid, 1234, null);

        Assert.True(result.Success);
        _log.Verify(l => l.Warn(It.Is<string>(message =>
            message.Contains("The transfer helper started", StringComparison.Ordinal)
            && message.Contains("warning text", StringComparison.Ordinal))), Times.Once);
    }

    [Fact]
    public void RunAndWait_WhenProcessFails_RetainsLogFile()
    {
        const string accountSid = "S-1-5-21-test";
        const string logFilePath = @"C:\temp\preftrans.log";

        _workspace.Setup(w => w.CreateLogFile(accountSid))
            .Returns(new PrefTransLogWorkspaceResult(true, logFilePath, null));
        _launchFacade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ProcessStarted, TestProcessInfoFactory.Empty()));
        _processWaiter.Setup(w => w.WaitForResult(
                It.IsAny<ProcessInfo>(),
                1234,
                logFilePath,
                null))
            .Returns(new SettingsTransferResult(false, "failed"));
        var launcher = CreateLauncher();

        var result = launcher.RunAndWait("preftrans.exe", "store", "settings.json", accountSid, 1234, null);

        Assert.False(result.Success);
        Assert.Equal("failed", result.Message);
        _workspace.Verify(w => w.TryDeleteLogFile(logFilePath), Times.Never);
    }

    [Fact]
    public void RunAndWait_WhenLaunchReturnsNoProcess_DeletesLogFileAndReturnsFailure()
    {
        const string accountSid = "S-1-5-21-test";
        const string logFilePath = @"C:\temp\preftrans.log";
        _workspace.Setup(w => w.CreateLogFile(accountSid))
            .Returns(new PrefTransLogWorkspaceResult(true, logFilePath, null));
        _launchFacade.Setup(f => f.LaunchFile(It.IsAny<ProcessLaunchTarget>(), It.IsAny<LaunchIdentity>(), null))
            .Returns(new LaunchExecutionResult(LaunchExecutionStatus.ShellWrappedNoProcess, null));
        _workspace.Setup(w => w.TryDeleteLogFile(logFilePath));
        var launcher = CreateLauncher();

        var result = launcher.RunAndWait("preftrans.exe", "store", "settings.json", accountSid, 1234, null);

        Assert.False(result.Success);
        Assert.Contains("did not return a process handle", result.Message, StringComparison.Ordinal);
        _workspace.Verify(w => w.TryDeleteLogFile(logFilePath), Times.Once);
    }

    private PrefTransLauncher CreateLauncher()
        => new(_launchFacade.Object, _log.Object, _workspace.Object, _processWaiter.Object);

}
