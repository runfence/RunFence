using System.IO.Pipes;
using Moq;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class JobKeeperClientProcessQueryTests
{
    private readonly Mock<IProcessIdentitySnapshotReader> _snapshotReader = new();
    private readonly JobKeeperClientProcessQuery _query;

    public JobKeeperClientProcessQueryTests()
    {
        _query = new JobKeeperClientProcessQuery(_snapshotReader.Object);
    }

    [Fact]
    public void QueryProcessInfo_UsesSingleSnapshotForAllFields()
    {
        _snapshotReader.Setup(r => r.TryReadProcessIdentity(123))
            .Returns(new ProcessIdentitySnapshot(
                @"C:\Program Files\RunFence\RunFence.JobKeeper.exe",
                "S-1-5-21-1-2-3-1001",
                0x2000));

        var result = _query.QueryProcessInfo(123);

        Assert.Equal(@"C:\Program Files\RunFence\RunFence.JobKeeper.exe", result.ImagePath);
        Assert.Equal("S-1-5-21-1-2-3-1001", result.OwnerSid?.Value);
        Assert.Equal(0x2000, result.IntegrityLevel);
        _snapshotReader.Verify(r => r.TryReadProcessIdentity(123), Times.Once);
        _snapshotReader.VerifyNoOtherCalls();
    }

    [Fact]
    public void QueryProcessInfo_UnavailableSnapshot_ReturnsDefault()
    {
        _snapshotReader.Setup(r => r.TryReadProcessIdentity(123)).Returns((ProcessIdentitySnapshot?)null);

        var result = _query.QueryProcessInfo(123);

        Assert.Null(result.ImagePath);
        Assert.Null(result.OwnerSid);
        Assert.Null(result.IntegrityLevel);
    }

    [Fact]
    public void QueryProcessInfo_InvalidOwnerSid_TreatsOwnerAsUnavailable()
    {
        _snapshotReader.Setup(r => r.TryReadProcessIdentity(123))
            .Returns(new ProcessIdentitySnapshot(
                @"C:\Program Files\RunFence\RunFence.JobKeeper.exe",
                "not-a-sid",
                0x2000));

        var result = _query.QueryProcessInfo(123);

        Assert.Equal(@"C:\Program Files\RunFence\RunFence.JobKeeper.exe", result.ImagePath);
        Assert.Null(result.OwnerSid);
        Assert.Equal(0x2000, result.IntegrityLevel);
    }

    [Fact]
    public void QueryProcessInfo_ValidImageWithUnavailableOwnerAndIntegrity_PreservesSnapshotShape()
    {
        _snapshotReader.Setup(r => r.TryReadProcessIdentity(123))
            .Returns(new ProcessIdentitySnapshot(
                @"C:\Program Files\RunFence\RunFence.JobKeeper.exe",
                null,
                null));

        var result = _query.QueryProcessInfo(123);

        Assert.Equal(@"C:\Program Files\RunFence\RunFence.JobKeeper.exe", result.ImagePath);
        Assert.Null(result.OwnerSid);
        Assert.Null(result.IntegrityLevel);
    }

    [Fact]
    public async Task TryGetPipeClientProcessId_ConnectedInProcessClient_ReturnsCurrentProcessId()
    {
        var pipeName = $"RunFenceTest_JobKeeperClientProcessQuery_{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        var serverWaitTask = server.WaitForConnectionAsync();
        await client.ConnectAsync(1000);
        await serverWaitTask;

        var success = _query.TryGetPipeClientProcessId(server, out var pid);

        Assert.True(success);
        Assert.Equal((uint)Environment.ProcessId, pid);
    }
}
