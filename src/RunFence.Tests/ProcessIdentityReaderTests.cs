using System.Diagnostics;
using Moq;
using RunFence.Core;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class ProcessIdentityReaderTests
{
    private readonly ProcessIdentityReader _reader = new(new Mock<ILoggingService>().Object);

    [Fact]
    public void TryOpenProcessForQuery_CurrentProcess_ReturnsReadableHandle()
    {
        var result = _reader.TryOpenProcessForQuery((uint)Environment.ProcessId, out var processHandle);

        using (processHandle)
        {
            Assert.True(result);
            Assert.False(processHandle.IsInvalid);
        }
    }

    [Fact]
    public void TryGetProcessElevation_CurrentProcess_ReturnsFalseForUnitTestProcess()
    {
        var result = _reader.TryGetProcessElevation((uint)Environment.ProcessId, out var isElevated);

        Assert.True(result);
        Assert.False(isElevated);
    }

    [Fact]
    public void TryGetProcessIntegrityLevel_CurrentProcess_ReturnsMediumIntegrity()
    {
        var result = _reader.TryGetProcessIntegrityLevel((uint)Environment.ProcessId, out var integrityLevel);

        Assert.True(result);
        Assert.Equal(NativeTokenHelper.MandatoryLevelMedium, integrityLevel);
    }

    [Fact]
    public void TryGetProcessCreationTimeUtcTicks_CurrentProcess_ReturnsLiveCreationTime()
    {
        using var process = Process.GetCurrentProcess();

        var result = _reader.TryGetProcessCreationTimeUtcTicks((uint)process.Id, out var creationTimeUtcTicks);

        Assert.True(result);
        Assert.InRange(
            creationTimeUtcTicks,
            process.StartTime.ToUniversalTime().Ticks - TimeSpan.FromSeconds(5).Ticks,
            process.StartTime.ToUniversalTime().Ticks + TimeSpan.FromSeconds(5).Ticks);
    }

    [Fact]
    public void TryGetProcessImagePath_CurrentProcess_ReturnsFullPath()
    {
        var imagePath = _reader.TryGetProcessImagePath((uint)Environment.ProcessId);

        Assert.False(string.IsNullOrWhiteSpace(imagePath));
        Assert.True(Path.IsPathRooted(imagePath));
        Assert.EndsWith(".exe", imagePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryReadProcessIdentity_CurrentProcess_ReturnsAvailableFields()
    {
        var snapshot = _reader.TryReadProcessIdentity((uint)Environment.ProcessId);
        var imagePath = _reader.TryGetProcessImagePath((uint)Environment.ProcessId);
        var ownerSid = _reader.TryGetProcessOwnerSid((uint)Environment.ProcessId);
        var hasIntegrity = _reader.TryGetProcessIntegrityLevel((uint)Environment.ProcessId, out var integrityLevel);

        Assert.True(snapshot.HasValue);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Value.ImagePath));
        Assert.True(Path.IsPathRooted(snapshot.Value.ImagePath));
        Assert.Equal(imagePath, snapshot.Value.ImagePath);
        Assert.Equal(ownerSid, snapshot.Value.OwnerSid);
        Assert.True(hasIntegrity);
        Assert.Equal(integrityLevel, snapshot.Value.IntegrityLevel);
        Assert.Equal(NativeTokenHelper.MandatoryLevelMedium, snapshot.Value.IntegrityLevel);
    }
}
