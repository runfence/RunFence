using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class ProfileRepairHelperTests
{
    private const string Sid = "S-1-5-21-1-2-3-1001";

    [Fact]
    public void ExecuteWithProfileRepair_UserConfirmsRepair_KillsAccountProcessesBeforeRegistryRepair()
    {
        var corruptedProfile = CreateCorruptedProfile();
        var callOrder = new List<string>();
        var context = CreateContext();
        context.ProfileCorruptionDetector
            .Setup(d => d.Detect(Sid))
            .Returns(corruptedProfile);
        context.Prompt
            .Setup(p => p.ConfirmRepair(Sid))
            .Callback(() => callOrder.Add("confirm"))
            .Returns(true);
        context.ProcessTerminationService
            .Setup(s => s.KillProcesses(Sid))
            .Callback(() => callOrder.Add("kill"))
            .Returns(new ProcessKillResult(2, 0));
        context.ProfileRegistryRepairer
            .Setup(r => r.Repair(corruptedProfile))
            .Callback(() => callOrder.Add("repair"))
            .Returns(true);
        context.Prompt
            .Setup(p => p.ConfirmRestartRunFence())
            .Callback(() => callOrder.Add("restart-prompt"))
            .Returns(false);

        Assert.Throws<OperationCanceledException>(
            () => context.Service.ExecuteWithProfileRepair<int>(
                () => throw new InvalidOperationException("launch failed"),
                Sid));

        Assert.Equal(["confirm", "kill", "repair", "restart-prompt"], callOrder);
        context.ProcessTerminationService.Verify(s => s.KillProcesses(Sid), Times.Once);
        context.ProfileRegistryRepairer.Verify(r => r.Repair(corruptedProfile), Times.Once);
    }

    [Fact]
    public void ExecuteWithProfileRepair_UserDeclinesRepair_DoesNotKillProcesses()
    {
        var context = CreateContext();
        context.ProfileCorruptionDetector
            .Setup(d => d.Detect(Sid))
            .Returns(CreateCorruptedProfile());
        context.Prompt
            .Setup(p => p.ConfirmRepair(Sid))
            .Returns(false);

        Assert.Throws<InvalidOperationException>(
            () => context.Service.ExecuteWithProfileRepair<int>(
                () => throw new InvalidOperationException("launch failed"),
                Sid));

        context.ProcessTerminationService.Verify(s => s.KillProcesses(It.IsAny<string>()), Times.Never);
        context.ProfileRegistryRepairer.Verify(r => r.Repair(It.IsAny<CorruptedProfile>()), Times.Never);
    }

    [Fact]
    public void ExecuteWithProfileRepair_ProcessTerminationFails_DoesNotRepairRegistry()
    {
        var context = CreateContext();
        context.ProfileCorruptionDetector
            .Setup(d => d.Detect(Sid))
            .Returns(CreateCorruptedProfile());
        context.Prompt
            .Setup(p => p.ConfirmRepair(Sid))
            .Returns(true);
        context.ProcessTerminationService
            .Setup(s => s.KillProcesses(Sid))
            .Returns(new ProcessKillResult(1, 1));
        context.Prompt
            .Setup(p => p.NotifyRepairFailed());

        Assert.Throws<InvalidOperationException>(
            () => context.Service.ExecuteWithProfileRepair<int>(
                () => throw new InvalidOperationException("launch failed"),
                Sid));

        context.ProfileRegistryRepairer.Verify(r => r.Repair(It.IsAny<CorruptedProfile>()), Times.Never);
        context.Prompt.Verify(p => p.NotifyRepairFailed(), Times.Once);
    }

    [Fact]
    public void ExecuteWithProfileRepair_NoCorruptionDetected_RethrowsOriginalLaunchFailure()
    {
        var context = CreateContext();
        context.ProfileCorruptionDetector
            .Setup(d => d.Detect(Sid))
            .Returns((CorruptedProfile?)null);

        Assert.Throws<InvalidOperationException>(
            () => context.Service.ExecuteWithProfileRepair<int>(
                () => throw new InvalidOperationException("launch failed"),
                Sid));

        context.Prompt.Verify(p => p.ConfirmRepair(It.IsAny<string>()), Times.Never);
        context.ProcessTerminationService.Verify(s => s.KillProcesses(It.IsAny<string>()), Times.Never);
    }

    private static CorruptedProfile CreateCorruptedProfile()
        => new(Sid, @"C:\Users\Target", @"C:\Users\TEMP.MACHINE.001");

    private static TestContext CreateContext()
    {
        var prompt = new Mock<IProfileRepairPrompt>(MockBehavior.Strict);
        var detector = new Mock<IProfileCorruptionDetector>(MockBehavior.Strict);
        var repairer = new Mock<IProfileRegistryRepairer>(MockBehavior.Strict);
        var processTermination = new Mock<IProcessTerminationService>(MockBehavior.Strict);
        var restartService = new Mock<IRunFenceRestartService>(MockBehavior.Strict);
        var log = new Mock<ILoggingService>();
        var service = new ProfileRepairHelper(
            prompt.Object,
            detector.Object,
            repairer.Object,
            processTermination.Object,
            restartService.Object,
            log.Object,
            new NTTranslateApi(log.Object));

        return new TestContext(
            service,
            prompt,
            detector,
            repairer,
            processTermination,
            restartService);
    }

    private sealed record TestContext(
        ProfileRepairHelper Service,
        Mock<IProfileRepairPrompt> Prompt,
        Mock<IProfileCorruptionDetector> ProfileCorruptionDetector,
        Mock<IProfileRegistryRepairer> ProfileRegistryRepairer,
        Mock<IProcessTerminationService> ProcessTerminationService,
        Mock<IRunFenceRestartService> RunFenceRestartService);
}
