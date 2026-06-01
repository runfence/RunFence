using Microsoft.Win32.SafeHandles;
using Moq;
using RunFence.Core;
using RunFence.ForegroundMarker;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class ForegroundProcessJobInspectorTests
{
    private readonly Mock<IProcessQueryHandleProvider> _processQueryHandleProvider = new();
    private readonly Mock<IJobObjectApi> _jobObjectApi = new();
    private readonly Mock<IVerifiedRestrictedJobCache> _verifiedRestrictedJobCache = new();

    [Fact]
    public void TryIsIsolated_ProcessNotInAnyJob_ReturnsNotInAnyJob()
    {
        var processHandle = OpenProcess(1234);
        _jobObjectApi.Setup(a => a.IsProcessInJob(processHandle.DangerousGetHandle(), IntPtr.Zero)).Returns(false);

        var inspector = CreateInspector();

        var result = inspector.TryIsIsolated(1234);

        Assert.Equal(ForegroundProcessJobInspectionResult.NotInAnyJob, result);
    }

    [Fact]
    public void TryIsIsolated_UnreadableNullJobQuery_ReturnsUnknown()
    {
        var processHandle = OpenProcess(1234);
        _jobObjectApi.Setup(a => a.IsProcessInJob(processHandle.DangerousGetHandle(), IntPtr.Zero)).Returns((bool?)null);

        var inspector = CreateInspector();

        var result = inspector.TryIsIsolated(1234);

        Assert.Equal(ForegroundProcessJobInspectionResult.Unknown, result);
    }

    [Fact]
    public void TryIsIsolated_NonIsolatingCandidate_DoesNotBlockLaterIsolatingCandidate()
    {
        var processHandle = OpenProcess(1234);
        _jobObjectApi.Setup(a => a.IsProcessInJob(processHandle.DangerousGetHandle(), IntPtr.Zero)).Returns(true);
        _verifiedRestrictedJobCache
            .Setup(c => c.CheckMembership(processHandle))
            .Returns(VerifiedRestrictedJobMembershipResult.Match);

        var inspector = CreateInspector();

        var result = inspector.TryIsIsolated(1234);

        Assert.Equal(ForegroundProcessJobInspectionResult.Isolated, result);
    }

    [Fact]
    public void TryIsIsolated_CandidateMembershipUnreadable_ReturnsUnknown()
    {
        var processHandle = OpenProcess(1234);
        _jobObjectApi.Setup(a => a.IsProcessInJob(processHandle.DangerousGetHandle(), IntPtr.Zero)).Returns(true);
        _verifiedRestrictedJobCache
            .Setup(c => c.CheckMembership(processHandle))
            .Returns(VerifiedRestrictedJobMembershipResult.Unknown);

        var inspector = CreateInspector();

        var result = inspector.TryIsIsolated(1234);

        Assert.Equal(ForegroundProcessJobInspectionResult.Unknown, result);
    }

    [Fact]
    public void TryIsIsolated_ProcessInUnverifiedJob_ReturnsNotInAnyJob()
    {
        var processHandle = OpenProcess(1234);
        _jobObjectApi.Setup(a => a.IsProcessInJob(processHandle.DangerousGetHandle(), IntPtr.Zero)).Returns(true);
        _verifiedRestrictedJobCache
            .Setup(c => c.CheckMembership(processHandle))
            .Returns(VerifiedRestrictedJobMembershipResult.NoMatch);

        var inspector = CreateInspector();

        var result = inspector.TryIsIsolated(1234);

        Assert.Equal(ForegroundProcessJobInspectionResult.NotInAnyJob, result);
    }

    private ForegroundProcessJobInspector CreateInspector() =>
        new(
            _processQueryHandleProvider.Object,
            _jobObjectApi.Object,
            _verifiedRestrictedJobCache.Object,
            Mock.Of<ILoggingService>());

    private SafeProcessHandle OpenProcess(uint pid)
    {
        var processHandle = new SafeProcessHandle(new IntPtr(100), ownsHandle: false);
        _processQueryHandleProvider.Setup(r => r.TryOpenProcessForQuery(pid, out processHandle)).Returns(true);
        return processHandle;
    }
}
