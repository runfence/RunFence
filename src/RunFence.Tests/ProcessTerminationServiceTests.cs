using System.Security.Principal;
using Moq;
using RunFence.Account;
using RunFence.Core;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class ProcessTerminationServiceTests
{
    [Fact]
    public void CloseProcess_CurrentProcessMatchingOwner_IsNotStale()
    {
        var service = CreateService();
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("No current SID.");
        var snapshot = new ProcessInfo(Environment.ProcessId, null, null, null);

        var result = service.CloseProcess(snapshot.Pid, snapshot.StartTimeUtcTicks, sid);

        Assert.NotEqual(ProcessActionStatus.StaleProcess, result.Status);
    }

    [Fact]
    public void CloseProcess_WrongOwner_ReturnsStaleProcess()
    {
        var service = CreateService();
        var snapshot = new ProcessInfo(Environment.ProcessId, null, null, null);

        var result = service.CloseProcess(snapshot.Pid, snapshot.StartTimeUtcTicks, "S-1-5-18");

        Assert.Equal(ProcessActionStatus.StaleProcess, result.Status);
    }

    [Fact]
    public void KillProcess_WrongOwner_ReturnsStaleProcess()
    {
        var service = CreateService();
        var snapshot = new ProcessInfo(Environment.ProcessId, null, null, null);

        var result = service.KillProcess(snapshot.Pid, snapshot.StartTimeUtcTicks, "S-1-5-18");

        Assert.Equal(ProcessActionStatus.StaleProcess, result.Status);
    }

    [Fact]
    public void CloseProcess_MissingPid_ReturnsStaleProcess()
    {
        var service = CreateService();
        var snapshot = new ProcessInfo(int.MaxValue, null, null, null);

        var result = service.CloseProcess(snapshot.Pid, snapshot.StartTimeUtcTicks, "S-1-5-18");

        Assert.Equal(ProcessActionStatus.StaleProcess, result.Status);
    }

    [Fact]
    public void CloseProcess_StartTimeMismatch_ReturnsStaleProcess()
    {
        var service = CreateService();
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("No current SID.");
        var snapshot = new ProcessInfo(Environment.ProcessId, null, null, 1);

        var result = service.CloseProcess(snapshot.Pid, snapshot.StartTimeUtcTicks, sid);

        Assert.Equal(ProcessActionStatus.StaleProcess, result.Status);
    }

    [Fact]
    public void KillProcess_StartTimeMismatch_ReturnsStaleProcess()
    {
        var service = CreateService();
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("No current SID.");
        var snapshot = new ProcessInfo(Environment.ProcessId, null, null, 1);

        var result = service.KillProcess(snapshot.Pid, snapshot.StartTimeUtcTicks, sid);

        Assert.Equal(ProcessActionStatus.StaleProcess, result.Status);
    }

    private static ProcessTerminationService CreateService()
    {
        var log = new Mock<ILoggingService>();
        return new ProcessTerminationService(log.Object);
    }
}
