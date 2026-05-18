using System.Runtime.InteropServices;
using System.Windows.Forms;
using Moq;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Security;
using RunFence.Tests.Helpers;
using Xunit;

namespace RunFence.Tests;

public class InputInjectionBlockerServiceTests : IDisposable
{
    private readonly Mock<ILoggingService> _log = new();
    private readonly FakeHookApi _hooks = new();
    private readonly FakeForegroundWindowResolver _foreground = new();
    private readonly FakeProcessOwnerSidReader _process = new();
    private readonly ManualUiTimerFactory _timerFactory = new();
    private readonly InputInjectionBlockerService _service;

    public InputInjectionBlockerServiceTests()
    {
        _service = new InputInjectionBlockerService(
            _log.Object,
            _hooks,
            _foreground,
            _process,
            _timerFactory);
    }

    public void Dispose() => _service.Dispose();

    [Theory]
    [InlineData(0x02u, true)]
    [InlineData(0x00u, false)]
    [InlineData(0x10u, false)]
    [InlineData(0x12u, true)]
    public void IsLowerIlInjected_ReturnsExpected(uint flags, bool expected)
    {
        Assert.Equal(expected, InputInjectionBlockerService.IsLowerIlInjected(flags));
    }

    [Fact]
    public void DefaultState_IsEnabled()
    {
        Assert.True(_service.IsEnabled);
    }

    [Fact]
    public void SetTimedDisable_UsesInjectedTimerFactory()
    {
        _service.SetTimedDisable(TimeSpan.FromSeconds(7));

        var timer = Assert.Single(_timerFactory.Timers);
        Assert.True(timer.Enabled);
        Assert.Equal(7000, timer.Interval);
        Assert.Equal(1, timer.StartCallCount);
    }

    [Fact]
    public void TimedDisable_TimerTick_ReEnablesBlockingAndDisposesTimer()
    {
        _service.SetTimedDisable(TimeSpan.FromSeconds(7));
        var timer = Assert.Single(_timerFactory.Timers);

        timer.Fire();

        Assert.True(_service.IsEnabled);
        Assert.Equal(1, timer.StopCallCount);
        Assert.Equal(1, timer.DisposeCallCount);
    }

    [Fact]
    public void SetTimedDisable_CalledTwice_DisposesOldTimer()
    {
        _service.SetTimedDisable(TimeSpan.FromMinutes(10));
        var first = Assert.Single(_timerFactory.Timers);

        _service.SetTimedDisable(TimeSpan.FromMinutes(5));

        Assert.Equal(1, first.StopCallCount);
        Assert.Equal(1, first.DisposeCallCount);
        Assert.Equal(2, _timerFactory.Timers.Count);
        Assert.False(_service.IsEnabled);
    }

    [Fact]
    public void SetTemporarilyDisabled_StopsRunningTimer()
    {
        _service.SetTimedDisable(TimeSpan.FromMinutes(10));
        var timer = Assert.Single(_timerFactory.Timers);

        _service.SetTemporarilyDisabled();

        Assert.False(_service.IsEnabled);
        Assert.Equal(1, timer.StopCallCount);
        Assert.Equal(1, timer.DisposeCallCount);
    }

    [Fact]
    public void ApplyConfigSetting_False_DisablesBlockingAndUnhooksInstalledHandles()
    {
        _hooks.KeyboardInstallResults.Enqueue((IntPtr)11);
        _hooks.MouseInstallResults.Enqueue((IntPtr)22);

        _service.ApplyConfigSetting(true);
        _service.ApplyConfigSetting(false);

        Assert.Equal(1, _hooks.KeyboardInstallCalls);
        Assert.Equal(1, _hooks.MouseInstallCalls);
        Assert.DoesNotContain(IntPtr.Zero, _hooks.Unhooked);
        Assert.Contains((IntPtr)11, _hooks.Unhooked);
        Assert.Contains((IntPtr)22, _hooks.Unhooked);
    }

    [Fact]
    public void RepeatedEnable_DoesNotDuplicateInstalledHooks()
    {
        _hooks.KeyboardInstallResults.Enqueue((IntPtr)11);
        _hooks.MouseInstallResults.Enqueue((IntPtr)22);

        _service.ApplyConfigSetting(true);
        _service.ApplyConfigSetting(true);

        Assert.Equal(1, _hooks.KeyboardInstallCalls);
        Assert.Equal(1, _hooks.MouseInstallCalls);
    }

    [Fact]
    public void ApplyConfigSetting_True_InstallsBothKeyboardAndMouseHooks()
    {
        _service.ApplyConfigSetting(true);

        Assert.Equal(1, _hooks.KeyboardInstallCalls);
        Assert.Equal(1, _hooks.MouseInstallCalls);
        Assert.NotNull(_hooks.KeyboardCallback);
        Assert.NotNull(_hooks.MouseCallback);
    }

    [Fact]
    public void KeyboardCallback_LowerIlInjectedUnsafeKey_Blocks()
    {
        _foreground.Info = new ForegroundWindowInfo((IntPtr)1, 123, "Edit");

        var result = InvokeKeyboardHook((uint)Keys.A, 0x02);

        Assert.Equal((IntPtr)1, result);
    }

    [Fact]
    public void KeyboardCallback_LowerIlInjectedAllowlistedKey_PassesThrough()
    {
        _foreground.Info = new ForegroundWindowInfo((IntPtr)1, 123, "Edit");
        _hooks.NextHookResult = (IntPtr)77;

        var result = InvokeKeyboardHook((uint)Keys.VolumeDown, 0x02);

        Assert.Equal((IntPtr)77, result);
    }

    [Fact]
    public void KeyboardCallback_NonLowerIl_PassesThrough()
    {
        _foreground.Info = new ForegroundWindowInfo((IntPtr)1, 123, "Edit");
        _hooks.NextHookResult = (IntPtr)77;

        var result = InvokeKeyboardHook((uint)Keys.A, 0x00);

        Assert.Equal((IntPtr)77, result);
    }

    [Fact]
    public void KeyboardCallback_ExemptProcess_PassesThrough()
    {
        _foreground.Info = new ForegroundWindowInfo((IntPtr)1, 123, "Edit");
        _process.OwnerSids[123] = "S-1-5-21-1";
        _service.UpdateExemptedSids(["S-1-5-21-1"]);
        _hooks.NextHookResult = (IntPtr)77;

        var result = InvokeKeyboardHook((uint)Keys.A, 0x02);

        Assert.Equal((IntPtr)77, result);
    }

    [Fact]
    public void KeyboardCallback_nCodeLessThanZero_PassesThrough()
    {
        _foreground.Info = new ForegroundWindowInfo((IntPtr)1, 123, "Edit");
        _hooks.NextHookResult = (IntPtr)77;

        var result = InvokeKeyboardHook((uint)Keys.A, 0x02, -1);

        Assert.Equal((IntPtr)77, result);
    }

    [Fact]
    public void KeyboardCallback_EmptyExemptSet_IsExemptedProcessReturnsFalse()
    {
        _foreground.Info = new ForegroundWindowInfo((IntPtr)1, 123, "Edit");

        var result = InvokeKeyboardHook((uint)Keys.A, 0x02);

        Assert.Equal((IntPtr)1, result);
    }

    [Fact]
    public void KeyboardCallback_UnknownOwnerPid_IsExemptedProcessReturnsFalse()
    {
        _foreground.Info = new ForegroundWindowInfo((IntPtr)1, 123, "Edit");
        _process.OwnerSids[123] = "S-1-5-21-3";
        _service.UpdateExemptedSids(["S-1-5-21-1"]);

        var result = InvokeKeyboardHook((uint)Keys.A, 0x02);

        Assert.Equal((IntPtr)1, result);
    }

    [Fact]
    public void KeyboardCallback_PidZero_IsExemptedProcessReturnsFalse()
    {
        _foreground.Info = new ForegroundWindowInfo((IntPtr)1, 0, "Edit");

        var result = InvokeKeyboardHook((uint)Keys.A, 0x02);

        Assert.Equal((IntPtr)1, result);
    }

    [Fact]
    public void MouseCallback_ExemptProcess_PassesThrough()
    {
        _foreground.Info = new ForegroundWindowInfo((IntPtr)1, 123, "Edit");
        _process.OwnerSids[123] = "S-1-5-21-1";
        _service.UpdateExemptedSids(["S-1-5-21-1"]);
        _hooks.NextHookResult = (IntPtr)77;

        var result = InvokeMouseHook(0x02);

        Assert.Equal((IntPtr)77, result);
    }

    [Fact]
    public void MouseCallback_NonLowerIlInjected_PassesThrough()
    {
        _foreground.Info = new ForegroundWindowInfo((IntPtr)1, 123, "Edit");
        _hooks.NextHookResult = (IntPtr)77;

        var result = InvokeMouseHook(0x00);

        Assert.Equal((IntPtr)77, result);
    }

    [Fact]
    public void MouseCallback_LowerIlInjectedUnsafeFromNonExemptProcess_Blocks()
    {
        _foreground.Info = new ForegroundWindowInfo((IntPtr)1, 123, "Edit");

        var result = InvokeMouseHook(0x02);

        Assert.Equal((IntPtr)1, result);
    }

    [Fact]
    public void MouseCallback_nCodeLessThanZero_PassesThrough()
    {
        _foreground.Info = new ForegroundWindowInfo((IntPtr)1, 123, "Edit");
        _hooks.NextHookResult = (IntPtr)77;

        var result = InvokeMouseHook(0x02, -1);

        Assert.Equal((IntPtr)77, result);
    }

    [Fact]
    public void ReEnable_WhenConfigDisabled_StaysDisabled()
    {
        _service.ApplyConfigSetting(false);
        _service.SetTemporarilyDisabled();
        _service.ReEnable();

        Assert.False(_service.IsEnabled);
    }

    [Fact]
    public void Dispose_UnhooksInstalledHandles()
    {
        _hooks.KeyboardInstallResults.Enqueue((IntPtr)11);
        _hooks.MouseInstallResults.Enqueue((IntPtr)22);

        _service.ApplyConfigSetting(true);
        _service.Dispose();

        Assert.Contains((IntPtr)11, _hooks.Unhooked);
        Assert.Contains((IntPtr)22, _hooks.Unhooked);
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        var service = new InputInjectionBlockerService(
            _log.Object,
            _hooks,
            _foreground,
            _process,
            _timerFactory);

        var ex = Record.Exception(service.Dispose);

        Assert.Null(ex);
    }

    private IntPtr InvokeKeyboardHook(uint vkCode, uint flags, int nCode = 0)
    {
        EnsureHookCallbacksInstalled();
        var hookStruct = new WindowNative.KBDLLHOOKSTRUCT { vkCode = vkCode, flags = flags };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<WindowNative.KBDLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(hookStruct, ptr, false);
            return _hooks.KeyboardCallback!(nCode, (IntPtr)WindowNative.WM_KEYDOWN, ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private IntPtr InvokeMouseHook(uint flags, int nCode = 0)
    {
        EnsureHookCallbacksInstalled();
        var hookStruct = new WindowNative.MSLLHOOKSTRUCT { flags = flags };
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<WindowNative.MSLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(hookStruct, ptr, false);
            return _hooks.MouseCallback!(nCode, IntPtr.Zero, ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private void EnsureHookCallbacksInstalled()
    {
        if (_hooks.KeyboardCallback == null || _hooks.MouseCallback == null)
            _service.ApplyConfigSetting(true);
    }

    private sealed class FakeHookApi : ILowLevelHookApi
    {
        public Queue<IntPtr> KeyboardInstallResults { get; } = new();
        public Queue<IntPtr> MouseInstallResults { get; } = new();
        public List<IntPtr> Unhooked { get; } = [];
        public int KeyboardInstallCalls { get; private set; }
        public int MouseInstallCalls { get; private set; }
        public IntPtr NextHookResult { get; set; } = (IntPtr)99;
        public WindowNative.LowLevelKeyboardProc? KeyboardCallback { get; private set; }
        public WindowNative.LowLevelMouseProc? MouseCallback { get; private set; }

        public IntPtr InstallKeyboardHook(WindowNative.LowLevelKeyboardProc callback)
        {
            KeyboardInstallCalls++;
            KeyboardCallback = callback;
            return KeyboardInstallResults.Count > 0 ? KeyboardInstallResults.Dequeue() : (IntPtr)(100 + KeyboardInstallCalls);
        }

        public IntPtr InstallMouseHook(WindowNative.LowLevelMouseProc callback)
        {
            MouseInstallCalls++;
            MouseCallback = callback;
            return MouseInstallResults.Count > 0 ? MouseInstallResults.Dequeue() : (IntPtr)(200 + MouseInstallCalls);
        }

        public bool Unhook(IntPtr hook)
        {
            Unhooked.Add(hook);
            return true;
        }

        public IntPtr CallNextHook(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam) => NextHookResult;
    }

    private sealed class FakeForegroundWindowResolver : IForegroundWindowResolver
    {
        public ForegroundWindowInfo Info { get; set; }
        public ForegroundWindowInfo GetForegroundWindow() => Info;
    }

    private sealed class FakeProcessOwnerSidReader : IProcessOwnerSidReader
    {
        public Dictionary<uint, string> OwnerSids { get; } = [];

        public string? TryGetProcessOwnerSid(uint processId) =>
            OwnerSids.TryGetValue(processId, out var sid) ? sid : null;
    }
}
