using Moq;
using RunFence.Core;
using RunFence.ForegroundMarker;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class ForegroundPrivilegeClassificationWorkerTests
{
    private readonly Mock<IProcessCreationTimeReader> _processCreationTimeReader = new();
    private readonly Mock<IProcessImagePathReader> _processImagePathReader = new();
    private readonly Mock<IProcessPrivilegeStateReader> _processPrivilegeStateReader = new();
    private readonly Mock<IProcessOwnerSidReader> _processOwnerSidReader = new();
    private readonly Mock<IProcessQueryHandleProvider> _processQueryHandleProvider = new();
    private readonly Mock<IInteractiveUserSidResolver> _interactiveUserSidResolver = new();
    private readonly Mock<IJobObjectApi> _jobObjectApi = new();
    private readonly Mock<IVerifiedRestrictedJobCache> _verifiedRestrictedJobCache = new();
    private readonly Mock<ISidResolver> _sidResolver = new();
    private readonly Mock<IProfilePathResolver> _profilePathResolver = new();

    [Fact]
    public async Task ClassifyAsync_SamePidAndCreationTime_ReusesCachedClassification()
    {
        var request1 = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        var request2 = new ForegroundPrivilegeClassificationRequest(2, new IntPtr(20), 1234, 2);
        SetupLowIntegrityClassification(1234);
        SetupCreationTimeSequence(1234, [100, 100, 100, 100, 100, 100]);

        var worker = CreateWorker();

        var first = await worker.ClassifyAsync(request1, CancellationToken.None);
        var second = await worker.ClassifyAsync(request2, CancellationToken.None);

        Assert.True(first.IsCacheable);
        Assert.True(second.IsCacheable);
        Assert.Equal(ForegroundPrivilegeMarkerKind.LowIL, second.Kind);
        Assert.Equal(request2.RequestId, second.RequestId);
        Assert.Equal(request2.TrackedWindowHandle, second.TrackedWindowHandle);
        _processPrivilegeStateReader.Verify(r => r.TryGetProcessElevation(1234, out It.Ref<bool>.IsAny), Times.Once);
    }

    [Fact]
    public async Task ClassifyAsync_ChangedCreationTime_DoesNotReuseCache()
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        SetupLowIntegrityClassification(1234);
        SetupCreationTimeSequence(1234, [100, 100, 100, 200, 200, 200]);

        var worker = CreateWorker();

        _ = await worker.ClassifyAsync(request, CancellationToken.None);
        _ = await worker.ClassifyAsync(request, CancellationToken.None);

        _processPrivilegeStateReader.Verify(r => r.TryGetProcessElevation(1234, out It.Ref<bool>.IsAny), Times.Exactly(2));
    }

    [Fact]
    public async Task ClassifyAsync_UnreadableCreationTime_ReturnsNonCacheableLiveResult()
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        _processCreationTimeReader.Setup(r => r.TryGetProcessCreationTimeUtcTicks(1234, out It.Ref<long>.IsAny)).Returns(false);
        SetupLowIntegrityClassification(1234);
        _processImagePathReader.Setup(r => r.TryGetProcessImagePath(1234)).Returns("should-not-be-used.exe");
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(1234)).Returns("S-1-should-not-be-used");

        var worker = CreateWorker();

        var result = await worker.ClassifyAsync(request, CancellationToken.None);

        Assert.True(result.IsVisible);
        Assert.False(result.IsCacheable);
        Assert.Null(result.PrivilegeSubjectCreationTimeUtcTicks);
        Assert.Equal("PID 1234", result.Metadata?.ProcessName);
        Assert.Null(result.Metadata?.AccountSid);
        _processImagePathReader.Verify(r => r.TryGetProcessImagePath(1234), Times.Never);
        _processOwnerSidReader.Verify(r => r.TryGetProcessOwnerSid(1234), Times.Never);
    }

    [Fact]
    public async Task ClassifyAsync_FinalCreationTimeChanged_ReturnsStale()
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        SetupLowIntegrityClassification(1234);
        SetupCreationTimeSequence(1234, [100, 200]);

        var worker = CreateWorker();

        var result = await worker.ClassifyAsync(request, CancellationToken.None);

        Assert.True(result.IsStale);
        Assert.False(result.IsCacheable);
        Assert.Equal(100, result.PrivilegeSubjectCreationTimeUtcTicks);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task ClassifyAsync_CanceledBeforeClassification_ThrowsOperationCanceledException()
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        SetupCreationTimeSequence(1234, [100]);
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        var worker = CreateWorker();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            worker.ClassifyAsync(request, cancellationTokenSource.Token));
    }

    [Fact]
    public async Task ClassifyAsync_BasicClassificationDoesNotReuseCacheAcrossRequests()
    {
        var request1 = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        var request2 = new ForegroundPrivilegeClassificationRequest(2, new IntPtr(20), 1234, 2);
        SetupBasicClassification(1234);
        SetupCreationTimeSequence(1234, [100, 100, 100, 100, 100, 100]);

        var worker = CreateWorker();

        var first = await worker.ClassifyAsync(request1, CancellationToken.None);
        var second = await worker.ClassifyAsync(request2, CancellationToken.None);

        Assert.True(first.IsVisible);
        Assert.True(second.IsVisible);
        Assert.Equal(ForegroundPrivilegeMarkerKind.Basic, first.Kind);
        Assert.Equal(ForegroundPrivilegeMarkerKind.Basic, second.Kind);
        Assert.False(first.IsCacheable);
        Assert.False(second.IsCacheable);
        _processPrivilegeStateReader.Verify(r => r.TryGetProcessElevation(1234, out It.Ref<bool>.IsAny), Times.Exactly(2));
    }

    [Fact]
    public async Task ClassifyAsync_BasicVisibleResult_AttachesMetadata()
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        SetupBasicClassification(1234);
        SetupCreationTimeSequence(1234, [100, 100, 100]);
        _processImagePathReader.Setup(r => r.TryGetProcessImagePath(1234)).Returns(@"C:\Apps\chrome.exe");
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(1234)).Returns("S-1-basic");

        var worker = CreateWorker();

        var result = await worker.ClassifyAsync(request, CancellationToken.None);

        Assert.True(result.IsVisible);
        Assert.Equal(ForegroundPrivilegeMarkerKind.Basic, result.Kind);
        Assert.Equal("chrome.exe", result.Metadata?.ProcessName);
        Assert.Equal("S-1-basic", result.Metadata?.AccountSid);
    }

    [Fact]
    public async Task ClassifyAsync_IsolatedVisibleResult_AttachesMetadata()
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        SetupIsolatedClassification(1234);
        SetupCreationTimeSequence(1234, [100, 100, 100]);
        _processImagePathReader.Setup(r => r.TryGetProcessImagePath(1234)).Returns("isolated.exe");
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(1234)).Returns("S-1-isolated");

        var worker = CreateWorker();

        var result = await worker.ClassifyAsync(request, CancellationToken.None);

        Assert.True(result.IsVisible);
        Assert.Equal(ForegroundPrivilegeMarkerKind.Isolated, result.Kind);
        Assert.Equal("isolated.exe", result.Metadata?.ProcessName);
        Assert.Equal("S-1-isolated", result.Metadata?.AccountSid);
    }

    [Fact]
    public async Task ClassifyAsync_LowIntegrityVisibleResult_AttachesMetadata()
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        SetupLowIntegrityClassification(1234);
        SetupCreationTimeSequence(1234, [100, 100, 100]);
        _processImagePathReader.Setup(r => r.TryGetProcessImagePath(1234)).Returns("lowil.exe");
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(1234)).Returns("S-1-lowil");

        var worker = CreateWorker();

        var result = await worker.ClassifyAsync(request, CancellationToken.None);

        Assert.True(result.IsVisible);
        Assert.Equal(ForegroundPrivilegeMarkerKind.LowIL, result.Kind);
        Assert.Equal("lowil.exe", result.Metadata?.ProcessName);
        Assert.Equal("S-1-lowil", result.Metadata?.AccountSid);
    }

    [Fact]
    public async Task ClassifyAsync_MetadataLookupFailure_KeepsClassificationVisibleWithFallbacks()
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        SetupLowIntegrityClassification(1234);
        SetupCreationTimeSequence(1234, [100, 100, 100]);
        _processImagePathReader.Setup(r => r.TryGetProcessImagePath(1234)).Throws<UnauthorizedAccessException>();
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(1234)).Throws<InvalidOperationException>();

        var worker = CreateWorker();

        var result = await worker.ClassifyAsync(request, CancellationToken.None);

        Assert.True(result.IsVisible);
        Assert.Equal(ForegroundPrivilegeMarkerKind.LowIL, result.Kind);
        Assert.Equal("PID 1234", result.Metadata?.ProcessName);
        Assert.Null(result.Metadata?.AccountSid);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(System.ComponentModel.Win32Exception))]
    [InlineData(typeof(ObjectDisposedException))]
    public async Task ClassifyAsync_ProcessImageLookupTypedFailures_UseFallbackProcessName(Type exceptionType)
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        SetupLowIntegrityClassification(1234);
        SetupCreationTimeSequence(1234, [100, 100, 100]);
        _processImagePathReader.Setup(r => r.TryGetProcessImagePath(1234))
            .Throws(CreateLookupException(exceptionType));
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(1234)).Returns("S-1-owner");

        var worker = CreateWorker();

        var result = await worker.ClassifyAsync(request, CancellationToken.None);

        Assert.True(result.IsVisible);
        Assert.Equal("PID 1234", result.Metadata?.ProcessName);
        Assert.Equal("S-1-owner", result.Metadata?.AccountSid);
    }

    [Theory]
    [InlineData(typeof(InvalidOperationException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(System.ComponentModel.Win32Exception))]
    [InlineData(typeof(ObjectDisposedException))]
    public async Task ClassifyAsync_ProcessOwnerLookupTypedFailures_UseNullAccountSid(Type exceptionType)
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        SetupLowIntegrityClassification(1234);
        SetupCreationTimeSequence(1234, [100, 100, 100]);
        _processImagePathReader.Setup(r => r.TryGetProcessImagePath(1234)).Returns("chrome.exe");
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(1234))
            .Throws(CreateLookupException(exceptionType));

        var worker = CreateWorker();

        var result = await worker.ClassifyAsync(request, CancellationToken.None);

        Assert.True(result.IsVisible);
        Assert.Equal("chrome.exe", result.Metadata?.ProcessName);
        Assert.Null(result.Metadata?.AccountSid);
    }

    [Fact]
    public async Task ClassifyAsync_CachedVisibleClassification_ResolvesFreshMetadataPerReturnedResult()
    {
        var request1 = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        var request2 = new ForegroundPrivilegeClassificationRequest(2, new IntPtr(20), 1234, 2);
        SetupLowIntegrityClassification(1234);
        SetupCreationTimeSequence(1234, [100, 100, 100, 100, 100, 100]);
        _processImagePathReader.SetupSequence(r => r.TryGetProcessImagePath(1234))
            .Returns("first.exe")
            .Returns("second.exe");
        _processOwnerSidReader.SetupSequence(r => r.TryGetProcessOwnerSid(1234))
            .Returns("S-1-first")
            .Returns("S-1-second");

        var worker = CreateWorker();

        var first = await worker.ClassifyAsync(request1, CancellationToken.None);
        var second = await worker.ClassifyAsync(request2, CancellationToken.None);

        Assert.Equal(request1.RequestId, first.RequestId);
        Assert.Equal(request1.TrackedWindowHandle, first.TrackedWindowHandle);
        Assert.Equal(request1.EnabledGeneration, first.EnabledGeneration);
        Assert.Equal(request2.RequestId, second.RequestId);
        Assert.Equal(request2.TrackedWindowHandle, second.TrackedWindowHandle);
        Assert.Equal(request2.EnabledGeneration, second.EnabledGeneration);
        Assert.Equal((uint)1234, first.PrivilegeSubjectProcessId);
        Assert.Equal((uint)1234, second.PrivilegeSubjectProcessId);
        Assert.Equal((long)100, first.PrivilegeSubjectCreationTimeUtcTicks);
        Assert.Equal((long)100, second.PrivilegeSubjectCreationTimeUtcTicks);
        Assert.Equal("first.exe", first.Metadata?.ProcessName);
        Assert.Equal("S-1-first", first.Metadata?.AccountSid);
        Assert.Equal("second.exe", second.Metadata?.ProcessName);
        Assert.Equal("S-1-second", second.Metadata?.AccountSid);
        _processPrivilegeStateReader.Verify(r => r.TryGetProcessElevation(1234, out It.Ref<bool>.IsAny), Times.Once);
    }

    [Fact]
    public async Task ClassifyAsync_ProcessCreationTimeChangesDuringMetadataLookup_ReturnsStale()
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        SetupLowIntegrityClassification(1234);
        SetupCreationTimeSequence(1234, [100, 100, 200]);
        _processImagePathReader.Setup(r => r.TryGetProcessImagePath(1234)).Returns("chrome.exe");
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(1234)).Returns("S-1-change");

        var worker = CreateWorker();

        var result = await worker.ClassifyAsync(request, CancellationToken.None);

        Assert.True(result.IsVisible);
        Assert.True(result.IsStale);
        Assert.False(result.IsCacheable);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task ClassifyAsync_FailedPostMetadataCreationTimeRecheck_ReturnsStale()
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        SetupLowIntegrityClassification(1234);
        SetupCreationTimeReadFailureAfterInitialSuccess(1234, 100, 100);
        _processImagePathReader.Setup(r => r.TryGetProcessImagePath(1234)).Returns("chrome.exe");
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(1234)).Returns("S-1-failure");

        var worker = CreateWorker();

        var result = await worker.ClassifyAsync(request, CancellationToken.None);

        Assert.True(result.IsVisible);
        Assert.True(result.IsStale);
        Assert.False(result.IsCacheable);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task ClassifyAsync_NoCreationTimeVisibleResult_UsesFallbackMetadata()
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        SetupLowIntegrityClassification(1234);
        _processCreationTimeReader.Setup(r => r.TryGetProcessCreationTimeUtcTicks(1234, out It.Ref<long>.IsAny)).Returns(false);
        _processImagePathReader.Setup(r => r.TryGetProcessImagePath(1234)).Returns((string?)null);
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(1234)).Returns((string?)null);

        var worker = CreateWorker();

        var result = await worker.ClassifyAsync(request, CancellationToken.None);

        Assert.True(result.IsVisible);
        Assert.Null(result.PrivilegeSubjectCreationTimeUtcTicks);
        Assert.Equal("PID 1234", result.Metadata?.ProcessName);
        Assert.Null(result.Metadata?.AccountSid);
    }

    [Fact]
    public async Task ClassifyAsync_HiddenElevatedResult_AttachesMetadata()
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        SetupElevatedHiddenClassification(1234);
        SetupCreationTimeSequence(1234, [100, 100, 100]);
        _processImagePathReader.Setup(r => r.TryGetProcessImagePath(1234)).Returns(@"C:\Apps\admin.exe");
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(1234)).Returns("S-1-admin");

        var worker = CreateWorker();

        var result = await worker.ClassifyAsync(request, CancellationToken.None);

        Assert.False(result.IsVisible);
        Assert.Null(result.Kind);
        Assert.Equal(ForegroundPrivilegeTooltipMode.Elevated, result.TooltipMode);
        Assert.Equal("admin.exe", result.Metadata?.ProcessName);
        Assert.Equal("S-1-admin", result.Metadata?.AccountSid);
    }

    [Fact]
    public async Task ClassifyAsync_HiddenHighIlResult_AttachesMetadata()
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        SetupHighIntegrityHiddenClassification(1234);
        SetupCreationTimeSequence(1234, [100, 100, 100]);
        _processImagePathReader.Setup(r => r.TryGetProcessImagePath(1234)).Returns(@"C:\Apps\highil.exe");
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(1234)).Returns("S-1-highil");

        var worker = CreateWorker();

        var result = await worker.ClassifyAsync(request, CancellationToken.None);

        Assert.False(result.IsVisible);
        Assert.Null(result.Kind);
        Assert.Equal(ForegroundPrivilegeTooltipMode.HighIL, result.TooltipMode);
        Assert.Equal("highil.exe", result.Metadata?.ProcessName);
        Assert.Equal("S-1-highil", result.Metadata?.AccountSid);
    }

    [Fact]
    public async Task ClassifyAsync_HiddenZeroPid_DoesNotCarryMetadata()
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 0, 1);
        var worker = CreateWorker();

        var result = await worker.ClassifyAsync(request, CancellationToken.None);

        Assert.False(result.IsVisible);
        Assert.Null(result.TooltipMode);
        Assert.Null(result.Metadata);
    }

    [Fact]
    public void CachedForegroundPrivilegeClassification_FromResult_VisibleWithoutKind_Throws()
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        var result = new ForegroundPrivilegeClassificationResult(
            request.RequestId,
            request.TrackedWindowHandle,
            request.EnabledGeneration,
            true,
            null,
            null,
            1234,
            100,
            true,
            false);

        Assert.Throws<InvalidOperationException>(() => CachedForegroundPrivilegeClassification.FromResult(result));
    }

    [Fact]
    public void CachedForegroundPrivilegeClassification_FromResult_HiddenWithKind_Throws()
    {
        var request = new ForegroundPrivilegeClassificationRequest(1, new IntPtr(10), 1234, 1);
        var result = new ForegroundPrivilegeClassificationResult(
            request.RequestId,
            request.TrackedWindowHandle,
            request.EnabledGeneration,
            false,
            ForegroundPrivilegeMarkerKind.LowIL,
            ForegroundPrivilegeTooltipMode.LowIL,
            1234,
            100,
            false,
            false);

        Assert.Throws<InvalidOperationException>(() => CachedForegroundPrivilegeClassification.FromResult(result));
    }

    private ForegroundPrivilegeClassificationWorker CreateWorker() =>
        new(
            _processCreationTimeReader.Object,
            new ForegroundPrivilegeMarkerMetadataResolver(
                _processImagePathReader.Object,
                _processOwnerSidReader.Object),
            new ForegroundPrivilegeClassifier(
                _processPrivilegeStateReader.Object,
                _processOwnerSidReader.Object,
                _interactiveUserSidResolver.Object,
                new ForegroundProcessJobInspector(
                    _processQueryHandleProvider.Object,
                    _jobObjectApi.Object,
                    _verifiedRestrictedJobCache.Object,
                    Mock.Of<ILoggingService>()),
                new SidDisplayNameResolver(_sidResolver.Object, _profilePathResolver.Object),
                Mock.Of<ILoggingService>()));

    private void SetupCreationTimeSequence(uint pid, long[] values)
    {
        var queue = new Queue<long>(values);
        _processCreationTimeReader.Setup(r => r.TryGetProcessCreationTimeUtcTicks(pid, out It.Ref<long>.IsAny))
            .Returns((uint _, out long creationTimeUtcTicks) =>
            {
                creationTimeUtcTicks = queue.Dequeue();
                return true;
            });
    }

    private void SetupBasicClassification(uint pid)
    {
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessElevation(pid, out It.Ref<bool>.IsAny)).Returns((uint _, out bool elevated) =>
        {
            elevated = false;
            return true;
        });
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessIntegrityLevel(pid, out It.Ref<int>.IsAny)).Returns((uint _, out int integrityLevel) =>
        {
            integrityLevel = RunFence.Core.NativeTokenHelper.MandatoryLevelMedium;
            return true;
        });
        _processQueryHandleProvider.Setup(r => r.TryOpenProcessForQuery(pid, out It.Ref<Microsoft.Win32.SafeHandles.SafeProcessHandle>.IsAny))
            .Returns((uint _, out Microsoft.Win32.SafeHandles.SafeProcessHandle processHandle) =>
            {
                processHandle = new Microsoft.Win32.SafeHandles.SafeProcessHandle(new IntPtr(100), ownsHandle: false);
                return true;
            });
        _jobObjectApi.Setup(a => a.IsProcessInJob(new IntPtr(100), IntPtr.Zero)).Returns(false);
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns("interactive");
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(pid)).Returns("other");
    }

    private void SetupIsolatedClassification(uint pid)
    {
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessElevation(pid, out It.Ref<bool>.IsAny)).Returns((uint _, out bool elevated) =>
        {
            elevated = false;
            return true;
        });
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessIntegrityLevel(pid, out It.Ref<int>.IsAny)).Returns((uint _, out int integrityLevel) =>
        {
            integrityLevel = RunFence.Core.NativeTokenHelper.MandatoryLevelMedium;
            return true;
        });
        _processQueryHandleProvider.Setup(r => r.TryOpenProcessForQuery(pid, out It.Ref<Microsoft.Win32.SafeHandles.SafeProcessHandle>.IsAny))
            .Returns((uint _, out Microsoft.Win32.SafeHandles.SafeProcessHandle processHandle) =>
            {
                processHandle = new Microsoft.Win32.SafeHandles.SafeProcessHandle(new IntPtr(100), ownsHandle: false);
                return true;
            });
        _jobObjectApi.Setup(a => a.IsProcessInJob(new IntPtr(100), IntPtr.Zero)).Returns(true);
        _verifiedRestrictedJobCache
            .Setup(c => c.CheckMembership(It.IsAny<Microsoft.Win32.SafeHandles.SafeProcessHandle>()))
            .Returns(VerifiedRestrictedJobMembershipResult.Match);
    }

    private void SetupLowIntegrityClassification(uint pid)
    {
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessElevation(pid, out It.Ref<bool>.IsAny)).Returns((uint _, out bool elevated) =>
        {
            elevated = false;
            return true;
        });
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessIntegrityLevel(pid, out It.Ref<int>.IsAny)).Returns((uint _, out int integrityLevel) =>
        {
            integrityLevel = RunFence.Core.NativeTokenHelper.MandatoryLevelLow;
            return true;
        });
    }

    private void SetupElevatedHiddenClassification(uint pid)
    {
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessElevation(pid, out It.Ref<bool>.IsAny)).Returns((uint _, out bool elevated) =>
        {
            elevated = true;
            return true;
        });
    }

    private void SetupHighIntegrityHiddenClassification(uint pid)
    {
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessElevation(pid, out It.Ref<bool>.IsAny)).Returns((uint _, out bool elevated) =>
        {
            elevated = false;
            return true;
        });
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessIntegrityLevel(pid, out It.Ref<int>.IsAny)).Returns((uint _, out int integrityLevel) =>
        {
            integrityLevel = RunFence.Core.NativeTokenHelper.MandatoryLevelHigh;
            return true;
        });
    }

    private void SetupCreationTimeReadFailureAfterInitialSuccess(uint pid, params long[] successfulReads)
    {
        var queue = new Queue<long>(successfulReads);
        _processCreationTimeReader.Setup(r => r.TryGetProcessCreationTimeUtcTicks(pid, out It.Ref<long>.IsAny))
            .Returns((uint _, out long creationTimeUtcTicks) =>
            {
                if (queue.Count > 0)
                {
                    creationTimeUtcTicks = queue.Dequeue();
                    return true;
                }

                creationTimeUtcTicks = 0;
                return false;
            });
    }

    private static Exception CreateLookupException(Type exceptionType) =>
        exceptionType == typeof(InvalidOperationException) ? new InvalidOperationException() :
        exceptionType == typeof(UnauthorizedAccessException) ? new UnauthorizedAccessException() :
        exceptionType == typeof(System.ComponentModel.Win32Exception) ? new System.ComponentModel.Win32Exception() :
        exceptionType == typeof(ObjectDisposedException) ? new ObjectDisposedException("process") :
        throw new ArgumentOutOfRangeException(nameof(exceptionType), exceptionType, "Unsupported lookup exception type.");
}
