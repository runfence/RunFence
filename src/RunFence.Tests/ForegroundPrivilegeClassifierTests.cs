using Moq;
using RunFence.Core;
using RunFence.ForegroundMarker;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class ForegroundPrivilegeClassifierTests
{
    private readonly Mock<IProcessPrivilegeStateReader> _processPrivilegeStateReader = new();
    private readonly Mock<IProcessOwnerSidReader> _processOwnerSidReader = new();
    private readonly Mock<IProcessQueryHandleProvider> _processQueryHandleProvider = new();
    private readonly Mock<IInteractiveUserSidResolver> _interactiveUserSidResolver = new();
    private readonly Mock<IJobObjectApi> _jobObjectApi = new();
    private readonly Mock<IVerifiedRestrictedJobCache> _verifiedRestrictedJobCache = new();
    private readonly Mock<ISidResolver> _sidResolver = new();
    private readonly Mock<IProfilePathResolver> _profilePathResolver = new();

    [Fact]
    public void Classify_InteractiveMediumProcessWithoutIsolation_ReturnsHidden()
    {
        var request = CreateRequest();
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessElevation(1234, out It.Ref<bool>.IsAny)).Returns((uint _, out bool elevated) =>
        {
            elevated = false;
            return true;
        });
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessIntegrityLevel(1234, out It.Ref<int>.IsAny)).Returns((uint _, out int integrity) =>
        {
            integrity = NativeTokenHelper.MandatoryLevelMedium;
            return true;
        });
        var processHandle = new Microsoft.Win32.SafeHandles.SafeProcessHandle(new IntPtr(100), ownsHandle: false);
        _processQueryHandleProvider.Setup(r => r.TryOpenProcessForQuery(1234, out processHandle)).Returns(true);
        _jobObjectApi.Setup(a => a.IsProcessInJob(processHandle.DangerousGetHandle(), IntPtr.Zero)).Returns(false);
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns("interactive");
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(1234)).Returns("interactive");

        var classifier = CreateClassifier();

        var result = classifier.Classify(request);

        Assert.False(result.IsVisible);
        Assert.Null(result.Kind);
        Assert.False(result.IsCacheable);
    }

    [Fact]
    public void Classify_NonInteractiveMediumProcessInNoJob_ReturnsBasic()
    {
        var request = CreateRequest();
        SetupMediumNotElevatedNoJob();
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns("interactive");
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(1234)).Returns("other");

        var classifier = CreateClassifier();

        var result = classifier.Classify(request);

        Assert.True(result.IsVisible);
        Assert.Equal(ForegroundPrivilegeMarkerKind.Basic, result.Kind);
        Assert.False(result.IsCacheable);
    }

    [Fact]
    public void Classify_WhenSidNamesResolve_LogsDisplayNames()
    {
        var request = CreateRequest();
        SetupMediumNotElevatedNoJob();
        const string interactiveSid = "S-1-5-21-1-2-3-1001";
        const string ownerSid = "S-1-5-21-1-2-3-1020";
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns(interactiveSid);
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(1234)).Returns(ownerSid);
        _sidResolver.Setup(r => r.TryResolveName(interactiveSid)).Returns("PC\\InteractiveUser");
        _sidResolver.Setup(r => r.TryResolveName(ownerSid)).Returns("PC\\OtherUser");
        var log = new Mock<ILoggingService>();
        log.SetupGet(l => l.Enabled).Returns(true);
        log.SetupGet(l => l.Verbosity).Returns(RunFence.Core.Models.LogVerbosity.Debug);
        var classifier = CreateClassifier(log.Object);

        _ = classifier.Classify(request);

        log.Verify(
            l => l.Debug(It.Is<string>(message =>
                message.Contains("OtherUser", StringComparison.Ordinal)
                && message.Contains("InteractiveUser", StringComparison.Ordinal)
                && message.Contains(ownerSid, StringComparison.Ordinal)
                && message.Contains(interactiveSid, StringComparison.Ordinal))),
            Times.Once);
    }

    [Fact]
    public void Classify_NonInteractiveMediumProcessInUnverifiedJob_ReturnsBasic()
    {
        var request = CreateRequest();
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessElevation(1234, out It.Ref<bool>.IsAny)).Returns((uint _, out bool elevated) =>
        {
            elevated = false;
            return true;
        });
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessIntegrityLevel(1234, out It.Ref<int>.IsAny)).Returns((uint _, out int integrity) =>
        {
            integrity = NativeTokenHelper.MandatoryLevelMedium;
            return true;
        });
        var processHandle = new Microsoft.Win32.SafeHandles.SafeProcessHandle(new IntPtr(100), ownsHandle: false);
        _processQueryHandleProvider.Setup(r => r.TryOpenProcessForQuery(1234, out processHandle)).Returns(true);
        _jobObjectApi.Setup(a => a.IsProcessInJob(processHandle.DangerousGetHandle(), IntPtr.Zero)).Returns(true);
        _verifiedRestrictedJobCache
            .Setup(c => c.CheckMembership(processHandle))
            .Returns(VerifiedRestrictedJobMembershipResult.NoMatch);
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns("interactive");
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(1234)).Returns("other");

        var classifier = CreateClassifier();

        var result = classifier.Classify(request);

        Assert.True(result.IsVisible);
        Assert.Equal(ForegroundPrivilegeMarkerKind.Basic, result.Kind);
        Assert.False(result.IsCacheable);
    }

    [Fact]
    public void Classify_LowIntegrityOwnedByInteractiveUser_ReturnsLowIlWithoutBasicFiltering()
    {
        var request = CreateRequest();
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessElevation(1234, out It.Ref<bool>.IsAny)).Returns((uint _, out bool elevated) =>
        {
            elevated = false;
            return true;
        });
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessIntegrityLevel(1234, out It.Ref<int>.IsAny)).Returns((uint _, out int integrity) =>
        {
            integrity = NativeTokenHelper.MandatoryLevelLow;
            return true;
        });

        var classifier = CreateClassifier();

        var result = classifier.Classify(request);

        Assert.True(result.IsVisible);
        Assert.Equal(ForegroundPrivilegeMarkerKind.LowIL, result.Kind);
        _interactiveUserSidResolver.Verify(r => r.GetInteractiveUserSid(), Times.Never);
    }

    [Fact]
    public void Classify_LowIntegrityWhenOwnerUnreadableOrJobStateUnreadable_ReturnsLowIl()
    {
        var request = CreateRequest();
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessElevation(1234, out It.Ref<bool>.IsAny)).Returns((uint _, out bool elevated) =>
        {
            elevated = false;
            return true;
        });
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessIntegrityLevel(1234, out It.Ref<int>.IsAny)).Returns((uint _, out int integrity) =>
        {
            integrity = NativeTokenHelper.MandatoryLevelLow;
            return true;
        });

        var classifier = CreateClassifier();

        var result = classifier.Classify(request);

        Assert.True(result.IsVisible);
        Assert.Equal(ForegroundPrivilegeMarkerKind.LowIL, result.Kind);
    }

    [Fact]
    public void Classify_NonElevatedMediumProcessInVerifiedJob_ReturnsIsolatedWithoutInteractiveUserFiltering()
    {
        var request = CreateRequest();
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessElevation(1234, out It.Ref<bool>.IsAny)).Returns((uint _, out bool elevated) =>
        {
            elevated = false;
            return true;
        });
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessIntegrityLevel(1234, out It.Ref<int>.IsAny)).Returns((uint _, out int integrity) =>
        {
            integrity = NativeTokenHelper.MandatoryLevelMedium;
            return true;
        });
        var processHandle = new Microsoft.Win32.SafeHandles.SafeProcessHandle(new IntPtr(100), ownsHandle: false);
        _processQueryHandleProvider.Setup(r => r.TryOpenProcessForQuery(1234, out processHandle)).Returns(true);
        _jobObjectApi.Setup(a => a.IsProcessInJob(processHandle.DangerousGetHandle(), IntPtr.Zero)).Returns(true);
        _verifiedRestrictedJobCache
            .Setup(c => c.CheckMembership(processHandle))
            .Returns(VerifiedRestrictedJobMembershipResult.Match);

        var classifier = CreateClassifier();

        var result = classifier.Classify(request);

        Assert.True(result.IsVisible);
        Assert.Equal(ForegroundPrivilegeMarkerKind.Isolated, result.Kind);
        _interactiveUserSidResolver.Verify(r => r.GetInteractiveUserSid(), Times.Never);
    }

    [Fact]
    public void Classify_HighIntegrityProcess_ReturnsHiddenHighIlBeforeJobOrOwnerChecks()
    {
        var request = CreateRequest();
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessElevation(1234, out It.Ref<bool>.IsAny)).Returns((uint _, out bool elevated) =>
        {
            elevated = false;
            return true;
        });
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessIntegrityLevel(1234, out It.Ref<int>.IsAny)).Returns((uint _, out int integrity) =>
        {
            integrity = NativeTokenHelper.MandatoryLevelHigh;
            return true;
        });

        var classifier = CreateClassifier();

        var result = classifier.Classify(request);

        Assert.False(result.IsVisible);
        Assert.Equal(ForegroundPrivilegeTooltipMode.HighIL, result.TooltipMode);
        _processQueryHandleProvider.Verify(
            r => r.TryOpenProcessForQuery(It.IsAny<uint>(), out It.Ref<Microsoft.Win32.SafeHandles.SafeProcessHandle>.IsAny),
            Times.Never);
        _interactiveUserSidResolver.Verify(r => r.GetInteractiveUserSid(), Times.Never);
        _processOwnerSidReader.Verify(r => r.TryGetProcessOwnerSid(It.IsAny<uint>()), Times.Never);
    }

    [Fact]
    public void Classify_ElevatedProcess_ReturnsHiddenBeforeAnyFurtherClassification()
    {
        var request = CreateRequest();
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessElevation(1234, out It.Ref<bool>.IsAny)).Returns((uint _, out bool elevated) =>
        {
            elevated = true;
            return true;
        });

        var classifier = CreateClassifier();

        var result = classifier.Classify(request);

        Assert.False(result.IsVisible);
        Assert.Equal(ForegroundPrivilegeTooltipMode.Elevated, result.TooltipMode);
        _processPrivilegeStateReader.Verify(r => r.TryGetProcessIntegrityLevel(It.IsAny<uint>(), out It.Ref<int>.IsAny), Times.Never);
    }

    [Fact]
    public void Classify_UnavailableInteractiveUserSid_SuppressesOnlyBasic()
    {
        var request = CreateRequest();
        SetupMediumNotElevatedNoJob();
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns((string?)null);

        var classifier = CreateClassifier();

        var result = classifier.Classify(request);

        Assert.False(result.IsVisible);
        Assert.False(result.IsCacheable);
    }

    [Fact]
    public void Classify_UnavailableOwnerSid_SuppressesOnlyBasic()
    {
        var request = CreateRequest();
        SetupMediumNotElevatedNoJob();
        _interactiveUserSidResolver.Setup(r => r.GetInteractiveUserSid()).Returns("interactive");
        _processOwnerSidReader.Setup(r => r.TryGetProcessOwnerSid(1234)).Returns((string?)null);

        var classifier = CreateClassifier();

        var result = classifier.Classify(request);

        Assert.False(result.IsVisible);
    }

    [Fact]
    public void Classify_UnreadableElevation_ReturnsHidden()
    {
        var request = CreateRequest();
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessElevation(1234, out It.Ref<bool>.IsAny)).Returns(false);

        var classifier = CreateClassifier();

        var result = classifier.Classify(request);

        Assert.False(result.IsVisible);
    }

    [Fact]
    public void Classify_UnreadableIntegrity_ReturnsHidden()
    {
        var request = CreateRequest();
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessElevation(1234, out It.Ref<bool>.IsAny)).Returns((uint _, out bool elevated) =>
        {
            elevated = false;
            return true;
        });
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessIntegrityLevel(1234, out It.Ref<int>.IsAny)).Returns(false);

        var classifier = CreateClassifier();

        var result = classifier.Classify(request);

        Assert.False(result.IsVisible);
        _processQueryHandleProvider.Verify(r => r.TryOpenProcessForQuery(It.IsAny<uint>(), out It.Ref<Microsoft.Win32.SafeHandles.SafeProcessHandle>.IsAny), Times.Never);
        _interactiveUserSidResolver.Verify(r => r.GetInteractiveUserSid(), Times.Never);
        _processOwnerSidReader.Verify(r => r.TryGetProcessOwnerSid(It.IsAny<uint>()), Times.Never);
    }

    [Fact]
    public void Classify_UnknownJobState_ReturnsHidden()
    {
        var request = CreateRequest();
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessElevation(1234, out It.Ref<bool>.IsAny)).Returns((uint _, out bool elevated) =>
        {
            elevated = false;
            return true;
        });
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessIntegrityLevel(1234, out It.Ref<int>.IsAny)).Returns((uint _, out int integrity) =>
        {
            integrity = NativeTokenHelper.MandatoryLevelMedium;
            return true;
        });
        _jobObjectApi.Setup(a => a.IsProcessInJob(It.IsAny<IntPtr>(), IntPtr.Zero)).Returns((bool?)null);
        var processHandle = new Microsoft.Win32.SafeHandles.SafeProcessHandle(new IntPtr(100), ownsHandle: false);
        _processQueryHandleProvider.Setup(r => r.TryOpenProcessForQuery(1234, out processHandle)).Returns(true);

        var classifier = CreateClassifier();

        var result = classifier.Classify(request);

        Assert.False(result.IsVisible);
        _interactiveUserSidResolver.Verify(r => r.GetInteractiveUserSid(), Times.Never);
        _processOwnerSidReader.Verify(r => r.TryGetProcessOwnerSid(It.IsAny<uint>()), Times.Never);
    }

    private ForegroundPrivilegeClassifier CreateClassifier(ILoggingService? log = null) =>
        new(
            _processPrivilegeStateReader.Object,
            _processOwnerSidReader.Object,
            _interactiveUserSidResolver.Object,
            new ForegroundProcessJobInspector(
                _processQueryHandleProvider.Object,
                _jobObjectApi.Object,
                _verifiedRestrictedJobCache.Object,
                Mock.Of<ILoggingService>()),
            new SidDisplayNameResolver(_sidResolver.Object, _profilePathResolver.Object),
            log ?? Mock.Of<ILoggingService>());

    private static ForegroundPrivilegeClassificationRequest CreateRequest() =>
        new(1, new IntPtr(50), 1234, 2);

    private void SetupMediumNotElevatedNoJob()
    {
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessElevation(1234, out It.Ref<bool>.IsAny)).Returns((uint _, out bool elevated) =>
        {
            elevated = false;
            return true;
        });
        _processPrivilegeStateReader.Setup(r => r.TryGetProcessIntegrityLevel(1234, out It.Ref<int>.IsAny)).Returns((uint _, out int integrity) =>
        {
            integrity = NativeTokenHelper.MandatoryLevelMedium;
            return true;
        });
        var processHandle = new Microsoft.Win32.SafeHandles.SafeProcessHandle(new IntPtr(100), ownsHandle: false);
        _processQueryHandleProvider.Setup(r => r.TryOpenProcessForQuery(1234, out processHandle)).Returns(true);
        _jobObjectApi.Setup(a => a.IsProcessInJob(processHandle.DangerousGetHandle(), IntPtr.Zero)).Returns(false);
    }
}
