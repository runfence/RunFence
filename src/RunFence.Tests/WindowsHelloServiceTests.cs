using Moq;
using RunFence.Core;
using RunFence.Security;
using Xunit;

namespace RunFence.Tests;

public class WindowsHelloServiceTests
{
    [Fact]
    public async Task VerifyAsync_NonInteractivePath_UsesAsyncNativeRequest()
    {
        var log = new Mock<ILoggingService>();
        var context = new Mock<IWindowsHelloExecutionContext>();
        var native = new Mock<IWindowsHelloNative>();
        context.Setup(c => c.GetForegroundWindow()).Returns((IntPtr)123);
        context.Setup(c => c.GetInteractiveUserSid()).Returns((string?)null);
        native.Setup(n => n.RequestVerification("prompt", (IntPtr)123))
            .ReturnsAsync(new WindowsHelloNativeResult(WindowsHelloNativeStatus.Verified, null, null));

        var service = new WindowsHelloService(log.Object, context.Object, native.Object);

        var result = await service.VerifyAsync("prompt");

        Assert.Equal(HelloVerificationResult.Verified, result);
        native.Verify(n => n.RequestVerification("prompt", (IntPtr)123), Times.Once);
        native.Verify(n => n.RequestVerificationBlocking(It.IsAny<string>(), It.IsAny<IntPtr>()), Times.Never);
    }

    [Fact]
    public async Task VerifyAsync_InteractiveFallback_UsesBlockingNativeRequestBeforeRevert()
    {
        var log = new Mock<ILoggingService>();
        var context = new Mock<IWindowsHelloExecutionContext>();
        var native = new Mock<IWindowsHelloNative>();
        context.Setup(c => c.GetForegroundWindow()).Returns((IntPtr)123);
        context.Setup(c => c.GetInteractiveUserSid()).Returns("S-1-5-21-1");
        context.Setup(c => c.IsCurrentUserInteractive()).Returns(false);
        context.Setup(c => c.TryGetExplorerToken()).Returns((IntPtr)456);
        context.Setup(c => c.ImpersonateLoggedOnUser((IntPtr)456)).Returns(true);
        context.Setup(c => c.RevertToSelf()).Returns(true);

        var sequence = new MockSequence();
        native.InSequence(sequence)
            .Setup(n => n.RequestVerification("prompt", (IntPtr)123))
            .ReturnsAsync(new WindowsHelloNativeResult(WindowsHelloNativeStatus.Unavailable, null, "na"));
        native.InSequence(sequence)
            .Setup(n => n.RequestVerificationBlocking("prompt", (IntPtr)123))
            .Returns(new WindowsHelloNativeResult(WindowsHelloNativeStatus.Failed, null, "failed"));
        log.InSequence(sequence)
            .Setup(l => l.Error("Windows Hello verification failed for interactive user", It.IsAny<Exception?>()));
        context.InSequence(sequence)
            .Setup(c => c.RevertToSelf())
            .Returns(true);

        var service = new WindowsHelloService(log.Object, context.Object, native.Object);

        var result = await service.VerifyAsync("prompt");

        Assert.Equal(HelloVerificationResult.NotAvailable, result);
        native.Verify(n => n.RequestVerificationBlocking("prompt", (IntPtr)123), Times.Once);
        context.Verify(c => c.RevertToSelf(), Times.Once);
        context.Verify(c => c.CloseHandle((IntPtr)456), Times.Once);
    }
}
