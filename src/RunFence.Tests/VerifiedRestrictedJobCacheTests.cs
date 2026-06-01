using Microsoft.Win32.SafeHandles;
using Moq;
using RunFence.Core;
using RunFence.Infrastructure;
using System.Security.Principal;
using Xunit;

namespace RunFence.Tests;

public sealed class VerifiedRestrictedJobCacheTests
{
    private static readonly SecurityIdentifier TrustedOwner =
        new(WellKnownSidType.LocalSystemSid, null);

    private readonly Mock<IJobObjectApi> _jobObjectApi = new();
    private readonly Mock<ILoggingService> _log = new();

    [Fact]
    public void TryAddDuplicate_AdmitsVerifiedHandle_ThenSweepRemovesZeroMemberHandle()
    {
        var cache = CreateCache();
        var sourceHandle = new IntPtr(40);
        var duplicatedHandle = new IntPtr(41);
        SetupDuplicate(sourceHandle, duplicatedHandle);
        SetupAdmissibleHandle(duplicatedHandle);
        _jobObjectApi.Setup(a => a.QueryProcessIds(duplicatedHandle)).Returns([123]);

        var added = cache.TryAddDuplicate(sourceHandle);

        Assert.True(added);

        _jobObjectApi.Setup(a => a.QueryProcessIds(duplicatedHandle)).Returns([]);
        cache.SweepEmptyOrInvalidJobs();

        _jobObjectApi.Verify(a => a.CloseHandle(duplicatedHandle), Times.Once);
    }

    [Fact]
    public void Dispose_ClosesAllRemainingHandles()
    {
        var cache = CreateCache();
        var firstSource = new IntPtr(50);
        var first = new IntPtr(51);
        var secondSource = new IntPtr(52);
        var second = new IntPtr(53);
        SetupDuplicate(firstSource, first);
        SetupDuplicate(secondSource, second);
        SetupAdmissibleHandle(first);
        SetupAdmissibleHandle(second);

        Assert.True(cache.TryAddDuplicate(firstSource));
        Assert.True(cache.TryAddDuplicate(secondSource));

        cache.Dispose();

        _jobObjectApi.Verify(a => a.CloseHandle(first), Times.Once);
        _jobObjectApi.Verify(a => a.CloseHandle(second), Times.Once);
    }

    [Fact]
    public void TryAddDuplicate_DuplicateHandle_ReturnsFalseAndDisposesRejectedDuplicate()
    {
        var cache = CreateCache();
        var firstSource = new IntPtr(60);
        var first = new IntPtr(61);
        var duplicateSource = new IntPtr(62);
        var duplicate = new IntPtr(63);
        SetupDuplicate(firstSource, first);
        SetupDuplicate(duplicateSource, duplicate);
        SetupAdmissibleHandle(first);
        SetupAdmissibleHandle(duplicate);
        _jobObjectApi.Setup(a => a.AreSameJobObject(first, duplicate)).Returns(true);
        _jobObjectApi.Setup(a => a.AreSameJobObject(duplicate, first)).Returns(true);

        Assert.True(cache.TryAddDuplicate(firstSource));
        Assert.False(cache.TryAddDuplicate(duplicateSource));

        _jobObjectApi.Verify(a => a.CloseHandle(first), Times.Never);
        _jobObjectApi.Verify(a => a.CloseHandle(duplicate), Times.Once);
    }

    [Fact]
    public void TryAddDuplicate_DuplicatesAndOwnsHandleOnSuccess()
    {
        var cache = CreateCache();
        var sourceHandle = new IntPtr(64);
        var duplicatedHandle = new IntPtr(65);
        SetupAdmissibleHandle(duplicatedHandle);
        SetupDuplicate(sourceHandle, duplicatedHandle);

        var added = cache.TryAddDuplicate(sourceHandle);

        Assert.True(added);
        cache.Dispose();
        _jobObjectApi.Verify(a => a.CloseHandle(duplicatedHandle), Times.Once);
    }

    [Fact]
    public void TryAddDuplicate_MissingHandleUiRestriction_DoesNotLogRejection()
    {
        var cache = CreateCache();
        var sourceHandle = new IntPtr(66);
        var duplicatedHandle = new IntPtr(67);
        SetupDuplicate(sourceHandle, duplicatedHandle);
        _jobObjectApi.Setup(a => a.QueryUiRestrictions(duplicatedHandle)).Returns(0u);

        var added = cache.TryAddDuplicate(sourceHandle);

        Assert.False(added);
        _log.Verify(l => l.Info(It.IsAny<string>()), Times.Never);
        _log.Verify(l => l.Debug(It.IsAny<string>()), Times.Never);
        _jobObjectApi.Verify(a => a.CloseHandle(duplicatedHandle), Times.Once);
    }

    [Fact]
    public void CheckMembership_ReturnsMatchNoMatchAndUnknown()
    {
        var cache = CreateCache();
        var matchSource = new IntPtr(70);
        var matchHandle = new IntPtr(71);
        var unknownSource = new IntPtr(72);
        var unknownHandle = new IntPtr(73);
        SetupDuplicate(matchSource, matchHandle);
        SetupDuplicate(unknownSource, unknownHandle);
        SetupAdmissibleHandle(matchHandle);
        SetupAdmissibleHandle(unknownHandle);
        _jobObjectApi.Setup(a => a.AreSameJobObject(matchHandle, unknownHandle)).Returns(false);
        Assert.True(cache.TryAddDuplicate(matchSource));
        Assert.True(cache.TryAddDuplicate(unknownSource));

        using var processHandle = new SafeProcessHandle(new IntPtr(100), ownsHandle: false);
        _jobObjectApi.Setup(a => a.IsProcessInJob(processHandle.DangerousGetHandle(), matchHandle)).Returns(true);
        Assert.Equal(VerifiedRestrictedJobMembershipResult.Match, cache.CheckMembership(processHandle));

        _jobObjectApi.Setup(a => a.IsProcessInJob(processHandle.DangerousGetHandle(), matchHandle)).Returns(false);
        _jobObjectApi.Setup(a => a.IsProcessInJob(processHandle.DangerousGetHandle(), unknownHandle)).Returns(false);
        Assert.Equal(VerifiedRestrictedJobMembershipResult.NoMatch, cache.CheckMembership(processHandle));

        _jobObjectApi.Setup(a => a.IsProcessInJob(processHandle.DangerousGetHandle(), unknownHandle)).Returns((bool?)null);
        Assert.Equal(VerifiedRestrictedJobMembershipResult.Unknown, cache.CheckMembership(processHandle));
    }

    [Fact]
    public void SweepEmptyOrInvalidJobs_RemovesHandleWhenSecurityOrUiStateFails()
    {
        var cache = CreateCache();
        var sourceHandle = new IntPtr(80);
        var handle = new IntPtr(81);
        SetupDuplicate(sourceHandle, handle);
        SetupAdmissibleHandle(handle);
        _jobObjectApi.Setup(a => a.QueryProcessIds(handle)).Returns([123]);
        Assert.True(cache.TryAddDuplicate(sourceHandle));

        _jobObjectApi.Setup(a => a.QueryUiRestrictions(handle)).Returns((uint?)0);
        cache.SweepEmptyOrInvalidJobs();

        _jobObjectApi.Verify(a => a.CloseHandle(handle), Times.Once);
    }

    private VerifiedRestrictedJobCache CreateCache() =>
        new(
            _jobObjectApi.Object,
            new VerifiedRestrictedJobAdmissionPolicy(_jobObjectApi.Object, _log.Object),
            _log.Object);

    private void SetupAdmissibleHandle(IntPtr handle)
    {
        _jobObjectApi.Setup(a => a.QueryUiRestrictions(handle)).Returns(ProcessJobManager.JobObjectUiLimitHandles);
        _jobObjectApi.Setup(a => a.QueryBasicLimitFlags(handle)).Returns(0u);
        _jobObjectApi.Setup(a => a.GetSecuritySnapshot(handle)).Returns(
            new JobObjectSecuritySnapshot(TrustedOwner, true, []));
    }

    private void SetupDuplicate(IntPtr sourceHandle, IntPtr duplicatedHandle)
    {
        _jobObjectApi.Setup(a => a.DuplicateHandleToProcess(
                ProcessNative.GetCurrentProcess(),
                sourceHandle,
                ProcessNative.GetCurrentProcess(),
                ProcessJobManager.JobObjectReconnectAccess,
                out duplicatedHandle))
            .Returns(true);
    }
}
