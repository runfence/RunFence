using System.ComponentModel;
using Moq;
using RunFence.Core;
using RunFence.Launch;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowsAppsRegistrationRepairRunnerTests
{
    private const string Sid = "S-1-5-21-100-200-300-1001";

    private readonly Mock<IWindowsAppsPackageRegistrationRepairer> _repairer = new();
    private readonly Mock<IWindowsAppsRepairProcessLauncher> _launcher = new();
    private readonly Mock<ILoggingService> _log = new();

    [Fact]
    public void TryRepair_NoRepairTarget_ReturnsFalse()
    {
        var failedTarget = new ProcessLaunchTarget(@"C:\Apps\App.exe");
        var originalIdentity = new AccountLaunchIdentity(Sid);
        var resolvedIdentity = new AccountLaunchIdentity(Sid);
        var runner = CreateRunner();

        var result = runner.TryRepair(failedTarget, originalIdentity, resolvedIdentity);

        Assert.False(result);
        _launcher.Verify(
            l => l.LaunchRepair(It.IsAny<ProcessLaunchTarget>(), It.IsAny<AccountLaunchIdentity>(), It.IsAny<AccountLaunchIdentity>()),
            Times.Never);
    }

    [Fact]
    public void TryRepair_ExitCodeZero_ReturnsTrue()
    {
        var failedTarget = new ProcessLaunchTarget(NotepadPackageExe());
        var repairTarget = new ProcessLaunchTarget("powershell.exe", "-Command repair");
        var originalIdentity = new AccountLaunchIdentity(Sid);
        var resolvedIdentity = new AccountLaunchIdentity(Sid);
        var process = new TestWindowsAppsRepairProcess(waitForExitResult: true, exitCode: 0);
        _repairer.Setup(r => r.TryCreateRepairTarget(failedTarget)).Returns(repairTarget);
        _launcher.Setup(l => l.LaunchRepair(repairTarget, originalIdentity, resolvedIdentity)).Returns(process);
        var runner = CreateRunner();

        var result = runner.TryRepair(failedTarget, originalIdentity, resolvedIdentity);

        Assert.True(result);
        Assert.True(process.DisposeCalled);
        Assert.Equal(30_000, process.WaitForExitTimeoutMs);
    }

    [Fact]
    public void TryRepair_ExitCodeNonZero_LogsWarningAndReturnsFalse()
    {
        var failedTarget = new ProcessLaunchTarget(NotepadPackageExe());
        var repairTarget = new ProcessLaunchTarget("powershell.exe", "-Command repair");
        var originalIdentity = new AccountLaunchIdentity(Sid);
        var resolvedIdentity = new AccountLaunchIdentity(Sid);
        var process = new TestWindowsAppsRepairProcess(waitForExitResult: true, exitCode: 9);
        _repairer.Setup(r => r.TryCreateRepairTarget(failedTarget)).Returns(repairTarget);
        _launcher.Setup(l => l.LaunchRepair(repairTarget, originalIdentity, resolvedIdentity)).Returns(process);
        var runner = CreateRunner();

        var result = runner.TryRepair(failedTarget, originalIdentity, resolvedIdentity);

        Assert.False(result);
        _log.Verify(l => l.Warn(It.Is<string>(message => message.Contains("exit code 9", StringComparison.Ordinal))), Times.Once);
    }

    [Fact]
    public void TryRepair_Timeout_KillsProcessLogsWarningAndReturnsFalse()
    {
        var failedTarget = new ProcessLaunchTarget(NotepadPackageExe());
        var repairTarget = new ProcessLaunchTarget("powershell.exe", "-Command repair");
        var originalIdentity = new AccountLaunchIdentity(Sid);
        var resolvedIdentity = new AccountLaunchIdentity(Sid);
        var process = new TestWindowsAppsRepairProcess(waitForExitResult: false, exitCode: 0);
        _repairer.Setup(r => r.TryCreateRepairTarget(failedTarget)).Returns(repairTarget);
        _launcher.Setup(l => l.LaunchRepair(repairTarget, originalIdentity, resolvedIdentity)).Returns(process);
        var runner = CreateRunner();

        var result = runner.TryRepair(failedTarget, originalIdentity, resolvedIdentity);

        Assert.False(result);
        Assert.True(process.KillCalled);
        _log.Verify(l => l.Warn(It.Is<string>(message => message.Contains("timed out", StringComparison.Ordinal))), Times.Once);
    }

    [Fact]
    public void TryRepair_KillThrows_LogsFailureAndReturnsFalse()
    {
        var failedTarget = new ProcessLaunchTarget(NotepadPackageExe());
        var repairTarget = new ProcessLaunchTarget("powershell.exe", "-Command repair");
        var originalIdentity = new AccountLaunchIdentity(Sid);
        var resolvedIdentity = new AccountLaunchIdentity(Sid);
        var process = new TestWindowsAppsRepairProcess(
            waitForExitResult: false,
            exitCode: 0,
            killException: new Win32Exception("kill failed"));
        _repairer.Setup(r => r.TryCreateRepairTarget(failedTarget)).Returns(repairTarget);
        _launcher.Setup(l => l.LaunchRepair(repairTarget, originalIdentity, resolvedIdentity)).Returns(process);
        var runner = CreateRunner();

        var result = runner.TryRepair(failedTarget, originalIdentity, resolvedIdentity);

        Assert.False(result);
        Assert.True(process.KillCalled);
        _log.Verify(l => l.Warn(It.Is<string>(message => message.Contains("kill failed", StringComparison.Ordinal))), Times.Once);
    }

    private WindowsAppsRegistrationRepairRunner CreateRunner()
        => new(_repairer.Object, _launcher.Object, _log.Object);

    private static string NotepadPackageExe() =>
        Path.Combine(
            @"C:\Program Files\WindowsApps",
            "Microsoft.WindowsNotepad_11.2512.29.0_x64__8wekyb3d8bbwe",
            "Notepad",
            "Notepad.exe");

    private sealed class TestWindowsAppsRepairProcess(
        bool waitForExitResult,
        int exitCode,
        Exception? killException = null) : IWindowsAppsRepairProcess
    {
        public int? WaitForExitTimeoutMs { get; private set; }

        public bool KillCalled { get; private set; }

        public bool DisposeCalled { get; private set; }

        public bool WaitForExit(int timeoutMs)
        {
            WaitForExitTimeoutMs = timeoutMs;
            return waitForExitResult;
        }

        public int ExitCode => exitCode;

        public void Kill()
        {
            KillCalled = true;
            if (killException != null)
                throw killException;
        }

        public void Dispose() => DisposeCalled = true;
    }
}
