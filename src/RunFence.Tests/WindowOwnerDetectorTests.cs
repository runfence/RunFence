using System.Security.Principal;
using Moq;
using RunFence.Core;
using RunFence.DragBridge;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class WindowOwnerDetectorTests
{
    private readonly Mock<IRestrictedJobInspector> _restrictedJobInspector = new();
    private readonly Mock<IWindowOwnerNativeReader> _nativeReader = new();
    private readonly Mock<IWindowOwnerProcessTokenReader> _processTokenReader = new();
    private static readonly SecurityIdentifier OwnerSid = new("S-1-5-21-1-2-3-1001");
    private static readonly SecurityIdentifier AppContainerSid = new("S-1-15-2-1");

    [Fact]
    public void GetForegroundWindowOwnerInfo_RestrictedJobTrue_ReturnsForegroundOwner()
    {
        SetupForeground(1234);
        SetupTokenInfo(1234, OwnerSid, AppContainerSid, NativeTokenHelper.MandatoryLevelHigh, isElevated: true);
        _restrictedJobInspector.Setup(i => i.IsProcessInHandleLimitedJob(1234)).Returns(true);

        var detector = CreateDetector();

        var result = detector.GetForegroundWindowOwnerInfo();

        Assert.NotNull(result);
        Assert.Equal(OwnerSid, result.Value.Sid);
        Assert.Equal(AppContainerSid, result.Value.AppContainerSid);
        Assert.Equal(NativeTokenHelper.MandatoryLevelHigh, result.Value.IntegrityLevel);
        Assert.True(result.Value.IsInRestrictedJob);
        Assert.True(result.Value.IsElevated);
    }

    [Fact]
    public void GetForegroundWindowOwnerInfo_RestrictedJobFalse_ReturnsForegroundOwner()
    {
        SetupForeground(1234);
        SetupTokenInfo(1234, OwnerSid, null, NativeTokenHelper.MandatoryLevelMedium);
        _restrictedJobInspector.Setup(i => i.IsProcessInHandleLimitedJob(1234)).Returns(false);

        var detector = CreateDetector();

        var result = detector.GetForegroundWindowOwnerInfo();

        Assert.NotNull(result);
        Assert.False(result.Value.IsInRestrictedJob);
        Assert.False(result.Value.IsAppContainer);
    }

    [Fact]
    public void GetForegroundWindowOwnerInfo_UnavailableOwnerSid_ReturnsNull()
    {
        SetupForeground(1234);
        _processTokenReader.Setup(r => r.TryGetTokenInfo(1234, out It.Ref<WindowOwnerProcessTokenInfo>.IsAny)).Returns(false);

        var detector = CreateDetector();

        Assert.Null(detector.GetForegroundWindowOwnerInfo());
        _restrictedJobInspector.Verify(i => i.IsProcessInHandleLimitedJob(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void GetDragSourceOrForegroundOwnerInfo_CaptureOwnerWinsOverForeground()
    {
        SetupForeground(100, hwnd: (IntPtr)10, threadId: 20);
        _nativeReader.Setup(r => r.TryGetCaptureWindowProcessId(20, (IntPtr)10, out It.Ref<uint>.IsAny))
            .Returns((uint _, IntPtr _, out uint processId) =>
            {
                processId = 200;
                return true;
            });
        SetupTokenInfo(200, new SecurityIdentifier("S-1-5-21-1-2-3-2000"), null, NativeTokenHelper.MandatoryLevelMedium);
        SetupTokenInfo(100, OwnerSid, null, NativeTokenHelper.MandatoryLevelHigh);

        var detector = CreateDetector();

        var result = detector.GetDragSourceOrForegroundOwnerInfo();

        Assert.NotNull(result);
        Assert.Equal("S-1-5-21-1-2-3-2000", result.Value.Sid.Value);
        _processTokenReader.Verify(r => r.TryGetTokenInfo(200, out It.Ref<WindowOwnerProcessTokenInfo>.IsAny), Times.Once);
        _processTokenReader.Verify(r => r.TryGetTokenInfo(100, out It.Ref<WindowOwnerProcessTokenInfo>.IsAny), Times.Never);
    }

    [Fact]
    public void GetDragSourceOrForegroundOwnerInfo_NoForegroundWindow_UsesCursorWindow()
    {
        _nativeReader.Setup(r => r.TryGetForegroundWindow(out It.Ref<IntPtr>.IsAny, out It.Ref<uint>.IsAny, out It.Ref<uint>.IsAny))
            .Returns(false);
        SetupCursorWindow(300);
        SetupTokenInfo(300, OwnerSid, null, NativeTokenHelper.MandatoryLevelMedium);

        var detector = CreateDetector();

        var result = detector.GetDragSourceOrForegroundOwnerInfo();

        Assert.NotNull(result);
        Assert.Equal(OwnerSid, result.Value.Sid);
    }

    [Fact]
    public void GetDragSourceOrForegroundOwnerInfo_ForegroundOwnerUnavailable_FallsBackToCursorWindow()
    {
        SetupForeground(100, hwnd: (IntPtr)10, threadId: 20);
        _nativeReader.Setup(r => r.TryGetCaptureWindowProcessId(20, (IntPtr)10, out It.Ref<uint>.IsAny)).Returns(false);
        _processTokenReader.Setup(r => r.TryGetTokenInfo(100, out It.Ref<WindowOwnerProcessTokenInfo>.IsAny)).Returns(false);
        SetupCursorWindow(300);
        SetupTokenInfo(300, new SecurityIdentifier("S-1-5-21-1-2-3-3000"), null, NativeTokenHelper.MandatoryLevelMedium);

        var detector = CreateDetector();

        var result = detector.GetDragSourceOrForegroundOwnerInfo();

        Assert.NotNull(result);
        Assert.Equal("S-1-5-21-1-2-3-3000", result.Value.Sid.Value);
    }

    [Fact]
    public void GetDragSourceOrForegroundOwnerInfo_CaptureOwnerUnavailable_FallsBackToForegroundThenCursor()
    {
        SetupForeground(100, hwnd: (IntPtr)10, threadId: 20);
        _nativeReader.Setup(r => r.TryGetCaptureWindowProcessId(20, (IntPtr)10, out It.Ref<uint>.IsAny))
            .Returns((uint _, IntPtr _, out uint processId) =>
            {
                processId = 200;
                return true;
            });
        _processTokenReader.Setup(r => r.TryGetTokenInfo(200, out It.Ref<WindowOwnerProcessTokenInfo>.IsAny)).Returns(false);
        _processTokenReader.Setup(r => r.TryGetTokenInfo(100, out It.Ref<WindowOwnerProcessTokenInfo>.IsAny)).Returns(false);
        SetupCursorWindow(300);
        SetupTokenInfo(300, OwnerSid, null, NativeTokenHelper.MandatoryLevelMedium);

        var detector = CreateDetector();

        var result = detector.GetDragSourceOrForegroundOwnerInfo();

        Assert.NotNull(result);
        Assert.Equal(OwnerSid, result.Value.Sid);
    }

    [Fact]
    public void GetDragSourceOrForegroundOwnerInfo_NoUsableCursorWindow_ReturnsNull()
    {
        _nativeReader.Setup(r => r.TryGetForegroundWindow(out It.Ref<IntPtr>.IsAny, out It.Ref<uint>.IsAny, out It.Ref<uint>.IsAny))
            .Returns(false);
        _nativeReader.Setup(r => r.TryGetCursorWindowProcessId(out It.Ref<uint>.IsAny)).Returns(false);

        var detector = CreateDetector();

        Assert.Null(detector.GetDragSourceOrForegroundOwnerInfo());
    }

    [Fact]
    public void GetForegroundWindowOwnerInfo_UnreadableIntegrity_UsesMediumIntegrityFallback()
    {
        SetupForeground(1234);
        SetupTokenInfo(1234, OwnerSid, null, null);

        var detector = CreateDetector();

        var result = detector.GetForegroundWindowOwnerInfo();

        Assert.NotNull(result);
        Assert.Equal(NativeTokenHelper.MandatoryLevelMedium, result.Value.IntegrityLevel);
    }

    private WindowOwnerDetector CreateDetector() =>
        new(_restrictedJobInspector.Object, _nativeReader.Object, _processTokenReader.Object);

    private void SetupForeground(uint processId, IntPtr? hwnd = null, uint threadId = 2)
    {
        _nativeReader.Setup(r => r.TryGetForegroundWindow(out It.Ref<IntPtr>.IsAny, out It.Ref<uint>.IsAny, out It.Ref<uint>.IsAny))
            .Returns((out IntPtr foregroundHwnd, out uint foregroundThreadId, out uint foregroundProcessId) =>
            {
                foregroundHwnd = hwnd ?? (IntPtr)50;
                foregroundThreadId = threadId;
                foregroundProcessId = processId;
                return true;
            });
    }

    private void SetupCursorWindow(uint processId)
    {
        _nativeReader.Setup(r => r.TryGetCursorWindowProcessId(out It.Ref<uint>.IsAny))
            .Returns((out uint cursorProcessId) =>
            {
                cursorProcessId = processId;
                return true;
            });
    }

    private void SetupTokenInfo(uint processId, SecurityIdentifier ownerSid, SecurityIdentifier? appContainerSid, int? integrityLevel, bool? isElevated = null)
    {
        _processTokenReader.Setup(r => r.TryGetTokenInfo(processId, out It.Ref<WindowOwnerProcessTokenInfo>.IsAny))
            .Returns((uint _, out WindowOwnerProcessTokenInfo info) =>
            {
                info = new WindowOwnerProcessTokenInfo(ownerSid, appContainerSid, integrityLevel, isElevated);
                return true;
            });
    }
}
