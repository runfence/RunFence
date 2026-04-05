using Moq;
using RunFence.Core;
using RunFence.DragBridge;
using Xunit;

namespace RunFence.Tests;

/// <summary>
/// Tests for GlobalHotkeyService. Registration lifecycle and event dispatch are tested via
/// SimulateHotkey; actual keystroke filtering (LLKHF_LOWER_IL_INJECTED) requires a live desktop.
/// </summary>
public class GlobalHotkeyServiceTests : IDisposable
{
    private readonly Mock<ILoggingService> _log;
    private readonly GlobalHotkeyService _service;

    // MOD_CONTROL | MOD_ALT
    private const int TestMods = 0x0003;
    private const int TestVk = 0x43; // C

    public GlobalHotkeyServiceTests()
    {
        _log = new Mock<ILoggingService>();
        _service = new GlobalHotkeyService(_log.Object);
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var service = new GlobalHotkeyService(_log.Object);
        var ex = Record.Exception(() => service.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void Register_DoesNotThrow()
    {
        var ex = Record.Exception(() => _service.Register(1, TestMods, TestVk));
        Assert.Null(ex);
    }

    [Fact]
    public void UnregisterAll_AllowsReRegistration()
    {
        _service.Register(1, TestMods, TestVk);
        _service.UnregisterAll();

        // Re-registration after unregister should not throw (return value may be true or false in test env)
        var ex = Record.Exception(() => _service.Register(1, TestMods, TestVk));
        Assert.Null(ex);
    }

    [Fact]
    public void Register_SameIdTwice_DoesNotThrow_AndUnregisterStillSucceeds()
    {
        // Register calls Unregister(id) internally before re-registering — verifying the
        // full register→re-register→unregister lifecycle does not throw is the observable
        // behavior in a unit test (hook installation may fail without a real message loop).
        _service.Register(1, TestMods, TestVk);
        var registerEx = Record.Exception(() => _service.Register(1, TestMods, 0x56 /* V */));
        Assert.Null(registerEx);
        var unregisterEx = Record.Exception(() => _service.Unregister(1));
        Assert.Null(unregisterEx);
    }

    [Fact]
    public void Unregister_UnregisteredId_DoesNotThrow()
    {
        var ex = Record.Exception(() => _service.Unregister(999));
        Assert.Null(ex);
    }

    // --- HotkeyPressed event dispatch (T6) ---

    [Fact]
    public void SimulateHotkey_RaisesHotkeyPressedWithCorrectId()
    {
        // Arrange
        int? receivedId = null;
        _service.HotkeyPressed += id => receivedId = id;

        // Act
        _service.SimulateHotkey(42);

        // Assert
        Assert.Equal(42, receivedId);
    }

    [Fact]
    public void SimulateHotkey_NoSubscribers_DoesNotThrow()
    {
        // No subscribers attached — must not throw
        var ex = Record.Exception(() => _service.SimulateHotkey(1));
        Assert.Null(ex);
    }

    [Fact]
    public void SimulateHotkey_MultipleSubscribers_AllReceiveEvent()
    {
        // Arrange
        int callCount = 0;
        _service.HotkeyPressed += _ => callCount++;
        _service.HotkeyPressed += _ => callCount++;

        // Act
        _service.SimulateHotkey(7);

        // Assert
        Assert.Equal(2, callCount);
    }

    [Fact]
    public void SimulateHotkey_AfterDispose_DoesNotThrow()
    {
        var service = new GlobalHotkeyService(_log.Object);
        service.Dispose();

        // SimulateHotkey after Dispose must not throw (no subscribers, no handle needed)
        var ex = Record.Exception(() => service.SimulateHotkey(1));
        Assert.Null(ex);
    }

    // --- ProcessKeystroke tests (T4.4) ---

    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const int MOD_CONTROL = 0x0002;
    private const int VK_CONTROL = 0x11;
    private const int VK_K = 0x4B;

    [Fact]
    public void ProcessKeystroke_CtrlK_Registered_ReturnsTrue()
    {
        // Arrange
        _service.Register(1, MOD_CONTROL, VK_K);

        // Act — press Ctrl then K
        _service.ProcessKeystroke(WM_KEYDOWN, VK_CONTROL);
        var consumed = _service.ProcessKeystroke(WM_KEYDOWN, VK_K);

        // Assert
        Assert.True(consumed);
    }

    [Fact]
    public void ProcessKeystroke_K_WithoutCtrl_ReturnsFalse()
    {
        // Arrange
        _service.Register(1, MOD_CONTROL, VK_K);

        // Act — press K without Ctrl
        var consumed = _service.ProcessKeystroke(WM_KEYDOWN, VK_K);

        // Assert
        Assert.False(consumed);
    }

    [Fact]
    public void ProcessKeystroke_CtrlReleasedThenK_ReturnsFalse()
    {
        // Arrange
        _service.Register(1, MOD_CONTROL, VK_K);

        // Act — press Ctrl, release Ctrl, then press K
        _service.ProcessKeystroke(WM_KEYDOWN, VK_CONTROL);
        _service.ProcessKeystroke(WM_KEYUP, VK_CONTROL);
        var consumed = _service.ProcessKeystroke(WM_KEYDOWN, VK_K);

        // Assert — modifier was released, so no match
        Assert.False(consumed);
    }
}