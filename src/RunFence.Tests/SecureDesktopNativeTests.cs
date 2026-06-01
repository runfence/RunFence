using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class SecureDesktopNativeTests
{
    [Fact]
    public void CaptureOriginalDesktop_WhenAvailable_RestoreAndClose_DoNotThrowAndSucceed()
    {
        var native = new SecureDesktopNative();

        var captureResult = native.CaptureOriginalDesktop();

        if (captureResult.Status == SecureDesktopNativeStatus.AccessDenied)
            return;

        Assert.Equal(SecureDesktopNativeStatus.Succeeded, captureResult.Status);
        Assert.NotEqual(IntPtr.Zero, captureResult.OpenedDesktopHandle);

        try
        {
            var restoreResult = native.RestoreDesktop(
                captureResult.OpenedDesktopHandle,
                captureResult.OriginalDesktopIdentity);

            if (restoreResult.Status == SecureDesktopNativeStatus.AccessDenied)
                return;

            Assert.Equal(SecureDesktopNativeStatus.Succeeded, restoreResult.Status);
        }
        finally
        {
            var closeResult = native.CloseDesktop(captureResult.OpenedDesktopHandle);
            if (closeResult.Status != SecureDesktopNativeStatus.AccessDenied)
                Assert.Equal(SecureDesktopNativeStatus.Succeeded, closeResult.Status);
        }
    }
}
