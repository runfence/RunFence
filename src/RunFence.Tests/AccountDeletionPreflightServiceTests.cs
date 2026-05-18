using Moq;
using RunFence.Account;
using RunFence.Account.UI;
using RunFence.Core;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class AccountDeletionPreflightServiceTests
{
    private const string Sid = "S-1-5-21-0-0-0-2001";

    [Fact]
    public async Task EnsureNoBlockingProcessesAsync_RunningProcessesAndKillConfirmed_KillsProcessesAndReturnsTrue()
    {
        var lifecycleManager = new Mock<IAccountLifecycleManager>(MockBehavior.Strict);
        lifecycleManager.SetupSequence(m => m.ValidateDeleteAsync(Sid))
            .ReturnsAsync(new AccountDeleteValidationResult(
                null,
                [
                    new ProcessInfo(44, @"C:\Apps\beta.exe", null),
                    new ProcessInfo(33, @"C:\Apps\alpha.exe", null)
                ]))
            .ReturnsAsync(AccountDeleteValidationResult.Success);

        var processTerminationService = new Mock<IProcessTerminationService>(MockBehavior.Strict);
        processTerminationService.Setup(s => s.KillProcesses(Sid))
            .Returns(new ProcessKillResult(2, 0));

        string? killPrompt = null;
        var messageBoxService = new Mock<IAccountMessageBoxService>(MockBehavior.Strict);
        messageBoxService.Setup(m => m.Show(
                null,
                It.IsAny<string>(),
                "Delete Account",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2))
            .Callback<IWin32Window?, string, string, MessageBoxButtons, MessageBoxIcon, MessageBoxDefaultButton>(
                (_, text, _, _, _, _) => killPrompt = text)
            .Returns(DialogResult.OK);

        var service = new AccountDeletionPreflightService(
            lifecycleManager.Object,
            processTerminationService.Object,
            messageBoxService.Object);

        var result = await service.EnsureNoBlockingProcessesAsync(new AccountDeletionPreflightRequest(Sid, "targetuser", false, false));

        Assert.True(result);
        Assert.NotNull(killPrompt);
        Assert.Contains("alpha.exe (PID 33)", killPrompt, StringComparison.Ordinal);
        Assert.Contains("beta.exe (PID 44)", killPrompt, StringComparison.Ordinal);
        processTerminationService.Verify(s => s.KillProcesses(Sid), Times.Once);
        lifecycleManager.Verify(m => m.ValidateDeleteAsync(Sid), Times.Exactly(2));
    }

    [Fact]
    public async Task EnsureNoBlockingProcessesAsync_RunningProcessesAndKillCanceled_StopsBeforeKilling()
    {
        var lifecycleManager = new Mock<IAccountLifecycleManager>(MockBehavior.Strict);
        lifecycleManager.Setup(m => m.ValidateDeleteAsync(Sid))
            .ReturnsAsync(new AccountDeleteValidationResult(
                null,
                [new ProcessInfo(55, @"C:\Apps\alpha.exe", null)]));

        var processTerminationService = new Mock<IProcessTerminationService>(MockBehavior.Strict);
        var messageBoxService = new Mock<IAccountMessageBoxService>(MockBehavior.Strict);
        messageBoxService.Setup(m => m.Show(
                null,
                It.IsAny<string>(),
                "Delete Account",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2))
            .Returns(DialogResult.Cancel);

        var service = new AccountDeletionPreflightService(
            lifecycleManager.Object,
            processTerminationService.Object,
            messageBoxService.Object);

        var result = await service.EnsureNoBlockingProcessesAsync(new AccountDeletionPreflightRequest(Sid, "targetuser", false, false));

        Assert.False(result);
        processTerminationService.Verify(s => s.KillProcesses(It.IsAny<string>()), Times.Never);
        lifecycleManager.Verify(m => m.ValidateDeleteAsync(Sid), Times.Once);
    }

    [Fact]
    public async Task EnsureNoBlockingProcessesAsync_PostKillValidationError_ShowsErrorAndReturnsFalse()
    {
        var lifecycleManager = new Mock<IAccountLifecycleManager>(MockBehavior.Strict);
        lifecycleManager.SetupSequence(m => m.ValidateDeleteAsync(Sid))
            .ReturnsAsync(new AccountDeleteValidationResult(
                null,
                [new ProcessInfo(44, @"C:\Apps\beta.exe", null)]))
            .ReturnsAsync(new AccountDeleteValidationResult(
                "Cannot delete the last administrator account.",
                Array.Empty<ProcessInfo>()));

        var processTerminationService = new Mock<IProcessTerminationService>(MockBehavior.Strict);
        processTerminationService.Setup(s => s.KillProcesses(Sid))
            .Returns(new ProcessKillResult(1, 0));

        var messageBoxService = new Mock<IAccountMessageBoxService>(MockBehavior.Strict);
        messageBoxService.Setup(m => m.Show(
                null,
                It.IsAny<string>(),
                "Delete Account",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2))
            .Returns(DialogResult.OK);
        messageBoxService.Setup(m => m.Show(
                null,
                "Cannot delete the last administrator account.",
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1))
            .Returns(DialogResult.OK);

        var service = new AccountDeletionPreflightService(
            lifecycleManager.Object,
            processTerminationService.Object,
            messageBoxService.Object);

        var result = await service.EnsureNoBlockingProcessesAsync(new AccountDeletionPreflightRequest(Sid, "targetuser", false, false));

        Assert.False(result);
        processTerminationService.Verify(s => s.KillProcesses(Sid), Times.Once);
        lifecycleManager.Verify(m => m.ValidateDeleteAsync(Sid), Times.Exactly(2));
    }

    [Theory]
    [InlineData(PathConstants.JobKeeperExeName)]
    [InlineData(PathConstants.ProfileKeeperExeName)]
    [InlineData(PathConstants.DragBridgeExeName)]
    [InlineData(PathConstants.PinHelperExeName)]
    [InlineData(PathConstants.LauncherExeName)]
    public async Task EnsureNoBlockingProcessesAsync_OnlyRunFenceHelperProcesses_KillsWithoutPrompt(string helperExeName)
    {
        var lifecycleManager = new Mock<IAccountLifecycleManager>(MockBehavior.Strict);
        lifecycleManager.SetupSequence(m => m.ValidateDeleteAsync(Sid))
            .ReturnsAsync(new AccountDeleteValidationResult(
                null,
                [
                    new ProcessInfo(44, Path.Combine(AppContext.BaseDirectory, helperExeName), null)
                ]))
            .ReturnsAsync(AccountDeleteValidationResult.Success);

        var processTerminationService = new Mock<IProcessTerminationService>(MockBehavior.Strict);
        processTerminationService.Setup(s => s.KillProcesses(Sid))
            .Returns(new ProcessKillResult(1, 0));

        var service = new AccountDeletionPreflightService(
            lifecycleManager.Object,
            processTerminationService.Object,
            new Mock<IAccountMessageBoxService>(MockBehavior.Strict).Object);

        var result = await service.EnsureNoBlockingProcessesAsync(new AccountDeletionPreflightRequest(Sid, "targetuser", false, false));

        Assert.True(result);
        processTerminationService.Verify(s => s.KillProcesses(Sid), Times.Once);
        lifecycleManager.Verify(m => m.ValidateDeleteAsync(Sid), Times.Exactly(2));
    }

    [Fact]
    public async Task EnsureNoBlockingProcessesAsync_OnlyConhostProcess_KillsWithoutPrompt()
    {
        var lifecycleManager = new Mock<IAccountLifecycleManager>(MockBehavior.Strict);
        lifecycleManager.SetupSequence(m => m.ValidateDeleteAsync(Sid))
            .ReturnsAsync(new AccountDeleteValidationResult(
                null,
                [
                    new ProcessInfo(44, Path.Combine(Environment.SystemDirectory, "conhost.exe"), null)
                ]))
            .ReturnsAsync(AccountDeleteValidationResult.Success);

        var processTerminationService = new Mock<IProcessTerminationService>(MockBehavior.Strict);
        processTerminationService.Setup(s => s.KillProcesses(Sid))
            .Returns(new ProcessKillResult(1, 0));

        var service = new AccountDeletionPreflightService(
            lifecycleManager.Object,
            processTerminationService.Object,
            new Mock<IAccountMessageBoxService>(MockBehavior.Strict).Object);

        var result = await service.EnsureNoBlockingProcessesAsync(
            new AccountDeletionPreflightRequest(Sid, "targetuser", false, false));

        Assert.True(result);
        processTerminationService.Verify(s => s.KillProcesses(Sid), Times.Once);
        lifecycleManager.Verify(m => m.ValidateDeleteAsync(Sid), Times.Exactly(2));
    }

    [Fact]
    public async Task EnsureNoBlockingProcessesAsync_HelperAndNonHelperProcesses_ShowsPromptBeforeKilling()
    {
        var lifecycleManager = new Mock<IAccountLifecycleManager>(MockBehavior.Strict);
        lifecycleManager.SetupSequence(m => m.ValidateDeleteAsync(Sid))
            .ReturnsAsync(new AccountDeleteValidationResult(
                null,
                [
                    new ProcessInfo(44, Path.Combine(AppContext.BaseDirectory, PathConstants.JobKeeperExeName), null),
                    new ProcessInfo(33, @"C:\Apps\alpha.exe", null)
                ]))
            .ReturnsAsync(AccountDeleteValidationResult.Success);

        var processTerminationService = new Mock<IProcessTerminationService>(MockBehavior.Strict);
        processTerminationService.Setup(s => s.KillProcesses(Sid))
            .Returns(new ProcessKillResult(2, 0));

        bool killPromptShown = false;
        var messageBoxService = new Mock<IAccountMessageBoxService>(MockBehavior.Strict);
        messageBoxService.Setup(m => m.Show(
                null,
                It.IsAny<string>(),
                "Delete Account",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2))
            .Callback(() => killPromptShown = true)
            .Returns(DialogResult.OK);

        var service = new AccountDeletionPreflightService(
            lifecycleManager.Object,
            processTerminationService.Object,
            messageBoxService.Object);

        var result = await service.EnsureNoBlockingProcessesAsync(new AccountDeletionPreflightRequest(Sid, "targetuser", false, false));

        Assert.True(result);
        Assert.True(killPromptShown);
        processTerminationService.Verify(s => s.KillProcesses(Sid), Times.Once);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task EnsureNoBlockingProcessesAsync_UnavailableOrSystemAccount_ShowsBlockingErrorWithoutKill(
        bool isUnavailable,
        bool isSystemSid)
    {
        var runningProcesses = new[]
        {
            new ProcessInfo(44, @"C:\Apps\beta.exe", null),
            new ProcessInfo(33, @"C:\Apps\alpha.exe", null)
        };
        var lifecycleManager = new Mock<IAccountLifecycleManager>(MockBehavior.Strict);
        lifecycleManager.Setup(m => m.ValidateDeleteAsync(Sid))
            .ReturnsAsync(new AccountDeleteValidationResult(null, runningProcesses));

        var processTerminationService = new Mock<IProcessTerminationService>(MockBehavior.Strict);
        string? errorMessage = null;
        var messageBoxService = new Mock<IAccountMessageBoxService>(MockBehavior.Strict);
        messageBoxService.Setup(m => m.Show(
                null,
                It.IsAny<string>(),
                "Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error,
                MessageBoxDefaultButton.Button1))
            .Callback<IWin32Window?, string, string, MessageBoxButtons, MessageBoxIcon, MessageBoxDefaultButton>(
                (_, text, _, _, _, _) => errorMessage = text)
            .Returns(DialogResult.OK);

        var service = new AccountDeletionPreflightService(
            lifecycleManager.Object,
            processTerminationService.Object,
            messageBoxService.Object);

        var result = await service.EnsureNoBlockingProcessesAsync(
            new AccountDeletionPreflightRequest(Sid, "targetuser", isUnavailable, isSystemSid));

        Assert.False(result);
        Assert.NotNull(errorMessage);
        Assert.Contains("Cannot delete this account while it has running processes:", errorMessage, StringComparison.Ordinal);
        Assert.Contains("alpha.exe (PID 33)", errorMessage, StringComparison.Ordinal);
        Assert.Contains("beta.exe (PID 44)", errorMessage, StringComparison.Ordinal);
        processTerminationService.Verify(s => s.KillProcesses(It.IsAny<string>()), Times.Never);
        lifecycleManager.Verify(m => m.ValidateDeleteAsync(Sid), Times.Once);
    }
}
