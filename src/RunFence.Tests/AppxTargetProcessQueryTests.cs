using System.Security.Principal;
using RunFence.AppxLauncher;
using RunFence.Launching.Processes;
using Xunit;

namespace RunFence.Tests;

public sealed class AppxTargetProcessQueryTests
{
    [Fact]
    public void GetTargetProcesses_CurrentProcessExecutable_ReturnsCurrentProcessIdentity()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.ProcessPath));
        var scanner = new ProcessSnapshotScanner();
        var query = new AppxTargetProcessQuery(
            (IProcessImageNameSnapshotReader)scanner,
            scanner,
            scanner);

        var processes = query.GetTargetProcesses(Environment.ProcessPath);

        var currentProcess = Assert.Single(processes, process => process.ProcessId == Environment.ProcessId);
        Assert.Equal(Environment.ProcessId, currentProcess.ProcessId);
        Assert.True(currentProcess.StartTimeUtc.HasValue);
        Assert.True(currentProcess.StartTimeUtc.Value <= DateTime.UtcNow);
        Assert.Equal(Path.GetFullPath(Environment.ProcessPath), Path.GetFullPath(currentProcess.ExecutablePath));
    }

    [Fact]
    public void GetProcessOwner_CurrentProcess_ReturnsCurrentUserSid()
    {
        var expectedSid = WindowsIdentity.GetCurrent().User?.Value;
        Assert.False(string.IsNullOrWhiteSpace(expectedSid));
        var scanner = new ProcessSnapshotScanner();
        var query = new AppxTargetProcessQuery(
            (IProcessImageNameSnapshotReader)scanner,
            scanner,
            scanner);

        var owner = query.GetProcessOwner(Environment.ProcessId, expectedSid);

        Assert.Equal(ProcessOwnerMatch.ExpectedOwner, owner.Match);
        Assert.Equal(expectedSid, owner.OwnerSid);
    }
}
