using System.Security.Principal;
using RunFence.Launching.Processes;
using Xunit;

namespace RunFence.Tests;

public sealed class ProcessSnapshotScannerTests
{
    [Fact]
    public void GetProcesses_CurrentProcess_ReturnsValidCurrentProcessData()
    {
        var scanner = new ProcessSnapshotScanner();

        var processes = scanner.GetProcesses();

        var currentProcess = Assert.Single(processes, process => process.ProcessId == Environment.ProcessId);
        Assert.Equal(Environment.ProcessId, currentProcess.ProcessId);
        Assert.True(currentProcess.CreationTimeUtcTicks.HasValue);
        Assert.True(currentProcess.CreationTimeUtcTicks.Value <= DateTime.UtcNow.Ticks);
    }

    [Fact]
    public void GetProcessesByImageName_CurrentProcess_ReturnsValidCurrentProcessData()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.ProcessPath));
        var scanner = new ProcessSnapshotScanner();
        var imageName = Path.GetFileName(Environment.ProcessPath);

        var processes = scanner.GetProcessesByImageName(imageName);

        var currentProcess = Assert.Single(processes, process => process.ProcessId == Environment.ProcessId);
        Assert.Equal(Environment.ProcessId, currentProcess.ProcessId);
        Assert.Equal(imageName, currentProcess.ImageName, StringComparer.OrdinalIgnoreCase);
        Assert.True(currentProcess.CreationTimeUtcTicks.HasValue);
        Assert.True(currentProcess.CreationTimeUtcTicks.Value <= DateTime.UtcNow.Ticks);
    }

    [Fact]
    public void GetExecutablePath_CurrentProcess_ReturnsExactProcessPath()
    {
        Assert.False(string.IsNullOrWhiteSpace(Environment.ProcessPath));
        var scanner = new ProcessSnapshotScanner();

        var executablePath = scanner.GetExecutablePath(Environment.ProcessId);

        Assert.False(string.IsNullOrWhiteSpace(executablePath));
        Assert.Equal(Path.GetFullPath(Environment.ProcessPath), Path.GetFullPath(executablePath));
    }

    [Fact]
    public void GetProcessOwner_CurrentProcess_ReturnsCurrentUserSid()
    {
        var expectedSid = WindowsIdentity.GetCurrent().User?.Value;
        Assert.False(string.IsNullOrWhiteSpace(expectedSid));
        var scanner = new ProcessSnapshotScanner();

        var owner = scanner.GetProcessOwner(Environment.ProcessId, expectedSid);

        Assert.Equal(ProcessOwnerMatch.ExpectedOwner, owner.Match);
        Assert.Equal(expectedSid, owner.OwnerSid);
    }

    [Fact]
    public void GetIntegrityLevel_CurrentProcess_ReturnsIntegrityRid()
    {
        var scanner = new ProcessSnapshotScanner();

        var integrityLevel = scanner.GetIntegrityLevel(Environment.ProcessId);

        Assert.True(integrityLevel >= 0x1000);
    }
}
