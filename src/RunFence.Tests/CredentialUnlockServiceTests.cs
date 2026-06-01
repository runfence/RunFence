using Moq;
using RunFence.Security;
using RunFence.Startup.UI;
using Xunit;

namespace RunFence.Tests;

public class CredentialUnlockServiceTests
{
    private readonly Mock<IUnlockPinPrompt> _unlockPinPrompt = new();
    private readonly Mock<IWindowsHelloNative> _helloNative = new();
    private readonly Mock<IWindowsHelloPinFallbackPrompt> _fallbackPrompt = new();
    private readonly Mock<IWindowsHelloWindowHandleProvider> _foregroundWindowProvider = new();
    private readonly CredentialUnlockService _service;
    private readonly IntPtr _foregroundWindow = new(1234);

    public CredentialUnlockServiceTests()
    {
        _foregroundWindowProvider.Setup(p => p.GetForegroundWindowHandle())
            .Returns(_foregroundWindow);
        _service = new CredentialUnlockService(
            _unlockPinPrompt.Object,
            _helloNative.Object,
            _fallbackPrompt.Object,
            _foregroundWindowProvider.Object);
    }

    [Fact]
    public void VerifyPin_PinVerified_ReturnsSucceeded()
    {
        _unlockPinPrompt.Setup(p => p.TryVerify()).Returns(true);

        var result = _service.VerifyPin();

        Assert.Equal(CredentialUnlockResult.Succeeded, result);
    }

[Fact]
    public void VerifyPin_PinRejected_ReturnsCanceled()
    {
        _unlockPinPrompt.Setup(p => p.TryVerify()).Returns(false);

        var result = _service.VerifyPin();

        Assert.Equal(CredentialUnlockResult.Canceled, result);
    }

[Fact]
    public async Task VerifyAsync_Pin_PinVerified_ReturnsSucceeded()
    {
        _unlockPinPrompt.Setup(p => p.TryVerify()).Returns(true);

        var result = await _service.VerifyAsync(CredentialUnlockMode.Pin);

        Assert.Equal(CredentialUnlockResult.Succeeded, result);
    }

[Fact]
    public async Task VerifyAsync_Pin_PinRejected_ReturnsCanceled()
    {
        _unlockPinPrompt.Setup(p => p.TryVerify()).Returns(false);

        var result = await _service.VerifyAsync(CredentialUnlockMode.Pin);

        Assert.Equal(CredentialUnlockResult.Canceled, result);
    }

    [Fact]
    public async Task VerifyAsync_WindowsHelloVerified_DoesNotPromptPin()
    {
        _helloNative.Setup(h => h.RequestVerification("Verify your identity to unlock RunFence", _foregroundWindow))
            .ReturnsAsync(new WindowsHelloNativeResult(WindowsHelloNativeStatus.Verified, null, null));

        var result = await _service.VerifyAsync(CredentialUnlockMode.WindowsHelloThenPin);

        Assert.Equal(CredentialUnlockResult.Succeeded, result);
        _unlockPinPrompt.Verify(p => p.TryVerify(), Times.Never);
    }

    [Fact]
    public async Task VerifyAsync_WindowsHelloVerified_UsesForegroundWindowHandleProvider()
    {
        _helloNative.Setup(h => h.RequestVerification("Verify your identity to unlock RunFence", _foregroundWindow))
            .ReturnsAsync(new WindowsHelloNativeResult(WindowsHelloNativeStatus.Verified, null, null));

        await _service.VerifyAsync(CredentialUnlockMode.WindowsHelloThenPin);

        _foregroundWindowProvider.Verify(p => p.GetForegroundWindowHandle(), Times.Once);
        _helloNative.Verify(
            h => h.RequestVerification("Verify your identity to unlock RunFence", _foregroundWindow),
            Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_WindowsHelloCanceled_DoesNotPromptPin_ReturnsCanceled()
    {
        _helloNative.Setup(h => h.RequestVerification(It.IsAny<string>(), It.IsAny<IntPtr>()))
            .ReturnsAsync(new WindowsHelloNativeResult(WindowsHelloNativeStatus.Canceled, null, "canceled"));

        var result = await _service.VerifyAsync(CredentialUnlockMode.WindowsHelloThenPin);

        Assert.Equal(CredentialUnlockResult.Canceled, result);
        _unlockPinPrompt.Verify(p => p.TryVerify(), Times.Never);
    }

    [Fact]
    public async Task VerifyAsync_WindowsHelloUnavailable_ConfirmFalse_DoesNotPromptPin_ReturnsCanceled()
    {
        _helloNative.Setup(h => h.RequestVerification(It.IsAny<string>(), It.IsAny<IntPtr>()))
            .ReturnsAsync(new WindowsHelloNativeResult(WindowsHelloNativeStatus.Unavailable, null, "unavailable"));
        _fallbackPrompt.Setup(f => f.ConfirmFallbackToPin(It.IsAny<WindowsHelloNativeResult>()))
            .Returns(false);

        var result = await _service.VerifyAsync(CredentialUnlockMode.WindowsHelloThenPin);

        Assert.Equal(CredentialUnlockResult.Canceled, result);
        _unlockPinPrompt.Verify(p => p.TryVerify(), Times.Never);
    }

    [Fact]
    public async Task VerifyAsync_WindowsHelloUnavailable_NoPromptSubscriber_DoesNotPromptPin_ReturnsCanceled()
    {
        _helloNative.Setup(h => h.RequestVerification(It.IsAny<string>(), It.IsAny<IntPtr>()))
            .ReturnsAsync(new WindowsHelloNativeResult(WindowsHelloNativeStatus.Unavailable, null, "unavailable"));

        var result = await _service.VerifyAsync(CredentialUnlockMode.WindowsHelloThenPin);

        Assert.Equal(CredentialUnlockResult.Canceled, result);
        _unlockPinPrompt.Verify(p => p.TryVerify(), Times.Never);
        _fallbackPrompt.Verify(f => f.ConfirmFallbackToPin(It.IsAny<WindowsHelloNativeResult>()), Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_WindowsHelloUnavailable_ConfirmTrue_PinSucceeds_ReturnsSucceeded()
    {
        _helloNative.Setup(h => h.RequestVerification(It.IsAny<string>(), It.IsAny<IntPtr>()))
            .ReturnsAsync(new WindowsHelloNativeResult(WindowsHelloNativeStatus.Unavailable, null, "unavailable"));
        _fallbackPrompt.Setup(f => f.ConfirmFallbackToPin(It.IsAny<WindowsHelloNativeResult>())).Returns(true);
        _unlockPinPrompt.Setup(p => p.TryVerify()).Returns(true);

        var result = await _service.VerifyAsync(CredentialUnlockMode.WindowsHelloThenPin);

        Assert.Equal(CredentialUnlockResult.Succeeded, result);
        _unlockPinPrompt.Verify(p => p.TryVerify(), Times.Once);
        _fallbackPrompt.Verify(f => f.ConfirmFallbackToPin(It.IsAny<WindowsHelloNativeResult>()), Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_WindowsHelloFailed_NoPromptSubscriber_DoesNotPromptPin_ReturnsCanceled()
    {
        _helloNative.Setup(h => h.RequestVerification(It.IsAny<string>(), It.IsAny<IntPtr>()))
            .ReturnsAsync(new WindowsHelloNativeResult(WindowsHelloNativeStatus.Failed, null, "failed"));

        var result = await _service.VerifyAsync(CredentialUnlockMode.WindowsHelloThenPin);

        Assert.Equal(CredentialUnlockResult.Canceled, result);
        _unlockPinPrompt.Verify(p => p.TryVerify(), Times.Never);
        _fallbackPrompt.Verify(f => f.ConfirmFallbackToPin(It.IsAny<WindowsHelloNativeResult>()), Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_WindowsHelloFailed_ConfirmTrue_PinFails_ReturnsCanceled()
    {
        _helloNative.Setup(h => h.RequestVerification(It.IsAny<string>(), It.IsAny<IntPtr>()))
            .ReturnsAsync(new WindowsHelloNativeResult(WindowsHelloNativeStatus.Failed, null, "failed"));
        _fallbackPrompt.Setup(f => f.ConfirmFallbackToPin(It.IsAny<WindowsHelloNativeResult>()))
            .Returns(true);
        _unlockPinPrompt.Setup(p => p.TryVerify()).Returns(false);

        var result = await _service.VerifyAsync(CredentialUnlockMode.WindowsHelloThenPin);

        Assert.Equal(CredentialUnlockResult.Canceled, result);
        _unlockPinPrompt.Verify(p => p.TryVerify(), Times.Once);
        _fallbackPrompt.Verify(f => f.ConfirmFallbackToPin(It.IsAny<WindowsHelloNativeResult>()), Times.Once);
    }

    [Fact]
    public async Task VerifyAsync_UnknownWindowsHelloStatus_ReturnsFailed()
    {
        _helloNative.Setup(h => h.RequestVerification(It.IsAny<string>(), It.IsAny<IntPtr>()))
            .ReturnsAsync(new WindowsHelloNativeResult((WindowsHelloNativeStatus)99, null, "unknown"));

        var result = await _service.VerifyAsync(CredentialUnlockMode.WindowsHelloThenPin);

        Assert.Equal(CredentialUnlockResult.Failed, result);
    }

    [Fact]
    public async Task VerifyAsync_UnknownCredentialMode_ReturnsFailed()
    {
        var result = await _service.VerifyAsync((CredentialUnlockMode)99);

        Assert.Equal(CredentialUnlockResult.Failed, result);
    }
}
