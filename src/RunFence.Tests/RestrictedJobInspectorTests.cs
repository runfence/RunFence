using Microsoft.Win32.SafeHandles;
using Moq;
using RunFence.Core;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class RestrictedJobInspectorTests
{
    private static readonly IntPtr JobHandle = new(21);

    private readonly Mock<IProcessQueryHandleProvider> _processQueryHandleProvider = new();
    private readonly Mock<IVerifiedRestrictedJobCache> _verifiedRestrictedJobCache = new();
    private readonly Mock<IJobObjectApi> _jobObjectApi = new();
    private readonly Mock<ILoggingService> _log = new();

    [Fact]
    public void IsProcessInHandleLimitedJob_CacheMatch_ReturnsTrue()
    {
        var processHandle = OpenProcess(1234);
        _jobObjectApi.Setup(a => a.IsProcessInJob(processHandle.DangerousGetHandle(), IntPtr.Zero)).Returns(true);
        _verifiedRestrictedJobCache
            .Setup(c => c.CheckMembership(processHandle))
            .Returns(VerifiedRestrictedJobMembershipResult.Match);

        var inspector = CreateInspector();

        Assert.True(inspector.IsProcessInHandleLimitedJob(1234));
    }

    [Fact]
    public void IsProcessInHandleLimitedJob_ProcessNotInAnyJob_ReturnsFalse()
    {
        var processHandle = OpenProcess(1234);
        _jobObjectApi.Setup(a => a.IsProcessInJob(processHandle.DangerousGetHandle(), IntPtr.Zero)).Returns(false);

        var inspector = CreateInspector();

        Assert.False(inspector.IsProcessInHandleLimitedJob(1234));
    }

    [Fact]
    public void IsProcessInHandleLimitedJob_CandidateWithoutHandleLimit_ReturnsFalse()
    {
        var processHandle = OpenProcess(1234);
        _jobObjectApi.Setup(a => a.IsProcessInJob(processHandle.DangerousGetHandle(), IntPtr.Zero)).Returns(true);
        _verifiedRestrictedJobCache
            .Setup(c => c.CheckMembership(processHandle))
            .Returns(VerifiedRestrictedJobMembershipResult.NoMatch);

        var inspector = CreateInspector();

        Assert.False(inspector.IsProcessInHandleLimitedJob(1234));
    }

    [Fact]
    public void IsProcessInHandleLimitedJob_CacheMissNeverFallsBackToSystemDiscovery_ReturnsFalse()
    {
        var processHandle = OpenProcess(1234);
        _jobObjectApi.Setup(a => a.IsProcessInJob(processHandle.DangerousGetHandle(), IntPtr.Zero)).Returns(true);
        _verifiedRestrictedJobCache
            .Setup(c => c.CheckMembership(processHandle))
            .Returns(VerifiedRestrictedJobMembershipResult.NoMatch);

        var inspector = CreateInspector();

        Assert.False(inspector.IsProcessInHandleLimitedJob(1234));
        _verifiedRestrictedJobCache.Verify(c => c.CheckMembership(processHandle), Times.Once);
    }

    private RestrictedJobInspector CreateInspector() =>
        new(
            _processQueryHandleProvider.Object,
            _verifiedRestrictedJobCache.Object,
            _jobObjectApi.Object,
            _log.Object);

    private SafeProcessHandle OpenProcess(uint pid)
    {
        var processHandle = new SafeProcessHandle(new IntPtr(100), ownsHandle: false);
        _processQueryHandleProvider.Setup(r => r.TryOpenProcessForQuery(pid, out processHandle)).Returns(true);
        return processHandle;
    }
}
