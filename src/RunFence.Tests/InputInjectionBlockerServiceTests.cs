using System.Runtime.InteropServices;
using Moq;
using RunFence.Core;
using RunFence.Infrastructure;
using RunFence.Security;
using System.Windows.Forms;
using Xunit;

namespace RunFence.Tests;

public class InputInjectionBlockerServiceTests : IDisposable
{
    private readonly Mock<ILoggingService> _log = new();
    private readonly FakeHookApi _hooks = new();
    private readonly FakeForegroundWindowResolver _foreground = new();
    private readonly FakeProcessIdentityReader _process = new();
    private readonly InputInjectionBlockerService _service;

    public InputInjectionBlockerServiceTests()
    {
        _service = new InputInjectionBlockerService(_log.Object, _hooks, _foreground, _process, _process);
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
    public void ApplyConfigSetting_False_DisablesBlocking()
    {
        _service.ApplyConfigSetting(false);

        Assert.False(_service.IsEnabled);
    }

    [Fact]
    public void SetTemporarilyDisabled_OverridesConfigEnabled()
    {
        _service.ApplyConfigSetting(true);

        _service.SetTemporarilyDisabled();

        Assert.False(_service.IsEnabled);
    }

    [Fact]
    public void ApplyConfigSetting_DoesNotResetTemporaryOverride()
    {
        _service.SetTemporarilyDisabled();

        _service.ApplyConfigSetting(true);

        Assert.False(_service.IsEnabled);
    }

    [Fact]
    public void ApplyConfigSetting_DoesNotResetTimedOverride()
    {
        _service.SetTimedDisable(TimeSpan.FromMinutes(10));

        _service.ApplyConfigSetting(true);

        Assert.False(_service.IsEnabled);
    }

    [Fact]
    public void ReEnable_ClearsTemporaryOverride()
    {
        _service.SetTemporarilyDisabled();

        _service.ReEnable();

        Assert.True(_service.IsEnabled);
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
    public void SetTimedDisable_CalledTwice_DisposesOldTimer()
    {
        var ex = Record.Exception(() =>
        {
            _service.SetTimedDisable(TimeSpan.FromMinutes(10));
            _service.SetTimedDisable(TimeSpan.FromMinutes(5));
        });

        Assert.Null(ex);
        Assert.False(_service.IsEnabled);
    }

    [Fact]
    public void SetTemporarilyDisabled_StopsRunningTimer()
    {
        _service.SetTimedDisable(TimeSpan.FromMinutes(10));

        _service.SetTemporarilyDisabled();

        Assert.False(_service.IsEnabled);
    }

    [Fact]
    public void Initialize_KeyboardFailureMouseSuccess_DisableUnhooksMouse()
    {
        _hooks.KeyboardInstallResults.Enqueue(IntPtr.Zero);
        _hooks.MouseInstallResults.Enqueue((IntPtr)22);

        _service.Initialize();
        _service.ApplyConfigSetting(false);

        Assert.Equal(1, _hooks.KeyboardInstallCalls);
        Assert.Equal(1, _hooks.MouseInstallCalls);
        Assert.DoesNotContain(IntPtr.Zero, _hooks.Unhooked);
        Assert.Contains((IntPtr)22, _hooks.Unhooked);
    }

    [Fact]
    public void Initialize_MouseFailureKeyboardSuccess_DisableUnhooksKeyboard()
    {
        _hooks.KeyboardInstallResults.Enqueue((IntPtr)11);
        _hooks.MouseInstallResults.Enqueue(IntPtr.Zero);

        _service.Initialize();
        _service.ApplyConfigSetting(false);

        Assert.Equal(1, _hooks.KeyboardInstallCalls);
        Assert.Equal(1, _hooks.MouseInstallCalls);
        Assert.Contains((IntPtr)11, _hooks.Unhooked);
        Assert.DoesNotContain(IntPtr.Zero, _hooks.Unhooked);
    }

    [Fact]
    public void RepeatedEnable_DoesNotDuplicateInstalledHooks()
    {
        _hooks.KeyboardInstallResults.Enqueue((IntPtr)11);
        _hooks.MouseInstallResults.Enqueue((IntPtr)22);

        _service.Initialize();
        _service.ApplyConfigSetting(true);
        _service.ReEnable();

        Assert.Equal(1, _hooks.KeyboardInstallCalls);
        Assert.Equal(1, _hooks.MouseInstallCalls);
    }

    [Fact]
    public void RepeatedEnable_InstallsOnlyMissingHook()
    {
        _hooks.KeyboardInstallResults.Enqueue(IntPtr.Zero);
        _hooks.KeyboardInstallResults.Enqueue((IntPtr)33);
        _hooks.MouseInstallResults.Enqueue((IntPtr)22);

        _service.Initialize();
        _service.ApplyConfigSetting(true);

        Assert.Equal(2, _hooks.KeyboardInstallCalls);
        Assert.Equal(1, _hooks.MouseInstallCalls);
    }

    [Fact]
    public void Disable_UnhooksEveryInstalledHandle()
    {
        _hooks.KeyboardInstallResults.Enqueue((IntPtr)11);
        _hooks.MouseInstallResults.Enqueue((IntPtr)22);

        _service.Initialize();
        _service.ApplyConfigSetting(false);

        Assert.Contains((IntPtr)11, _hooks.Unhooked);
        Assert.Contains((IntPtr)22, _hooks.Unhooked);
    }

    [Fact]
    public void Dispose_UnhooksEveryInstalledHandle()
    {
        _hooks.KeyboardInstallResults.Enqueue((IntPtr)11);
        _hooks.MouseInstallResults.Enqueue((IntPtr)22);

        _service.Initialize();
        _service.Dispose();

        Assert.Contains((IntPtr)11, _hooks.Unhooked);
        Assert.Contains((IntPtr)22, _hooks.Unhooked);
    }

    [Fact]
    public void KeyboardCallback_LowerIlInjectedUnsafeKey_Blocks()
    {
        _foreground.Info = new ForegroundWindowInfo((IntPtr)1, 123, "Edit");

        var result = InvokeKeyboardHook((uint)Keys.A, 0x02);

        Assert.Equal((IntPtr)1, result);
    }

    [Fact]
    public void KeyboardCallback_LowerIlInjectedAllowedKey_PassesThrough()
    {
        _foreground.Info = new ForegroundWindowInfo((IntPtr)1, 123, "Edit");
        _hooks.NextHookResult = (IntPtr)77;

        var result = InvokeKeyboardHook((uint)Keys.VolumeUp, 0x02);

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
    public void MouseCallback_LowerIlInjectedUnsafeProcess_Blocks()
    {
        _foreground.Info = new ForegroundWindowInfo((IntPtr)1, 123, "Edit");

        var result = InvokeMouseHook(0x02);

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
    public void Dispose_DoesNotThrow()
    {
        var service = new InputInjectionBlockerService(_log.Object, _hooks, _foreground, _process, _process);

        var ex = Record.Exception(() => service.Dispose());

        Assert.Null(ex);
    }

    private IntPtr InvokeKeyboardHook(uint vkCode, uint flags)
    {
        EnsureHookCallbacksInstalled();
        var hookStruct = new WindowNative.KBDLLHOOKSTRUCT { vkCode = vkCode, flags = flags };
        IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<WindowNative.KBDLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(hookStruct, ptr, false);
            return _hooks.KeyboardCallback!(0, (IntPtr)WindowNative.WM_KEYDOWN, ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private IntPtr InvokeMouseHook(uint flags)
    {
        EnsureHookCallbacksInstalled();
        var hookStruct = new WindowNative.MSLLHOOKSTRUCT { flags = flags };
        IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<WindowNative.MSLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(hookStruct, ptr, false);
            return _hooks.MouseCallback!(0, IntPtr.Zero, ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private void EnsureHookCallbacksInstalled()
    {
        if (_hooks.KeyboardCallback == null || _hooks.MouseCallback == null)
            _service.Initialize();
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

    private sealed class FakeProcessIdentityReader : IProcessIdentityReader
    {
        public Dictionary<uint, string> OwnerSids { get; } = [];
        public Dictionary<uint, string> ImageFileNames { get; } = [];

        public uint GetWindowProcessId(IntPtr hWnd) => 0;

        public bool TryGetConsoleHostProcessId(int processId, out int consoleHostProcessId)
        {
            consoleHostProcessId = 0;
            return false;
        }

        public string? TryGetProcessOwnerSid(uint processId) =>
            OwnerSids.TryGetValue(processId, out var sid) ? sid : null;

        public string? TryGetProcessImageFileName(uint processId) =>
            ImageFileNames.TryGetValue(processId, out var fileName) ? fileName : null;
    }
}
