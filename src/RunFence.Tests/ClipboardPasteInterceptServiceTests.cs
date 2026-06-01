using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Moq;
using RunFence.Core;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public class ClipboardPasteInterceptServiceTests : IDisposable
{
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const uint SyntheticPasteMarker = 0x52464350u;
    private const uint LLKHF_LOWER_IL_INJECTED = 0x02u;
    private const int VK_V = 0x56;
    private const int VK_INSERT = 0x2D;
    private const int VK_CONTROL = 0x11;
    private const int VK_SHIFT = 0x10;

    private readonly FakeKeyboardStateReader _keyboard = new();
    private readonly FakeTargetResolver _targetResolver = new();
    private readonly FakeClipboardFormatReader _clipboard = new();
    private readonly FakeRemoteProcessInjector _injector = new();
    private readonly FakeSyntheticInputSender _synthetic = new();
    private readonly FakeClipboardPasteWorkScheduler _workScheduler = new();
    private readonly FakeHookApi _hooks = new();
    private readonly Mock<ILoggingService> _log = new();
    private readonly ClipboardPasteInterceptService _service;

    public ClipboardPasteInterceptServiceTests()
    {
        _service = new ClipboardPasteInterceptService(
            new ClipboardPasteKeyDecision(_keyboard),
            _targetResolver,
            _clipboard,
            _injector,
            _synthetic,
            _workScheduler,
            _hooks,
            _log.Object);
        _workScheduler.RunInline = true;
    }

    public void Dispose() => _service.Dispose();

    [Fact]
    public void PasteKeyDecision_CtrlV_UsesInjectedKeyboardState()
    {
        _keyboard.DownKeys.Add(VK_CONTROL);
        var decision = new ClipboardPasteKeyDecision(_keyboard);

        Assert.Equal(ClipboardPasteKind.CtrlV, decision.Classify(WM_KEYDOWN, VK_V));
    }

    [Fact]
    public void PasteKeyDecision_ShiftInsert_UsesInjectedKeyboardState()
    {
        _keyboard.DownKeys.Add(VK_SHIFT);
        var decision = new ClipboardPasteKeyDecision(_keyboard);

        Assert.Equal(ClipboardPasteKind.ShiftInsert, decision.Classify(WM_KEYDOWN, VK_INSERT));
    }

    [Fact]
    public void HookCallback_SyntheticPasteMarker_NotIntercepted()
    {
        var result = InvokeHook(WM_KEYDOWN, VK_V, flags: 0, extraInfo: SyntheticPasteMarker);

        Assert.NotEqual((IntPtr)1, result);
        Assert.Equal(0, _targetResolver.ResolveCalls);
    }

    [Fact]
    public void HookCallback_LowerIlInjected_NotIntercepted()
    {
        var result = InvokeHook(WM_KEYDOWN, VK_V, flags: LLKHF_LOWER_IL_INJECTED, extraInfo: 0);

        Assert.NotEqual((IntPtr)1, result);
        Assert.Equal(0, _targetResolver.ResolveCalls);
    }

    [Fact]
    public void HookCallback_CtrlV_RestrictedTarget_InjectsAndSendsSyntheticPaste()
    {
        _keyboard.DownKeys.Add(VK_CONTROL);
        _targetResolver.Resolution = ClipboardPasteTargetResolution.Intercept(new ClipboardPasteTarget((IntPtr)100, 10, 10, 20));
        _clipboard.Formats = [new ClipboardFormatData(13, [1, 2, 3])];

        var result = InvokeHook(WM_KEYDOWN, VK_V, flags: 0, extraInfo: 0);

        Assert.Equal((IntPtr)1, result);
        Assert.Equal(1, _clipboard.ReadCalls);
        Assert.Equal(1, _injector.Calls);
        Assert.Equal(10, _injector.LastTargetProcessId);
        Assert.Equal(ClipboardPasteKind.CtrlV, _synthetic.LastPasteKind);
    }

    [Fact]
    public void HookCallback_ShiftInsert_RestrictedTarget_InjectsAndSendsSyntheticPaste()
    {
        _keyboard.DownKeys.Add(VK_SHIFT);
        _targetResolver.Resolution = ClipboardPasteTargetResolution.Intercept(new ClipboardPasteTarget((IntPtr)100, 10, 10, 20));
        _clipboard.Formats = [new ClipboardFormatData(13, [1])];

        var result = InvokeHook(WM_KEYDOWN, VK_INSERT, flags: 0, extraInfo: 0);

        Assert.Equal((IntPtr)1, result);
        Assert.Equal(1, _injector.Calls);
        Assert.Equal(ClipboardPasteKind.ShiftInsert, _synthetic.LastPasteKind);
    }

    [Fact]
    public void HookCallback_RestrictedTarget_InjectionFails_DoesNotSendSyntheticPaste()
    {
        _keyboard.DownKeys.Add(VK_CONTROL);
        _targetResolver.Resolution = ClipboardPasteTargetResolution.Intercept(new ClipboardPasteTarget((IntPtr)100, 10, 10, 20));
        _clipboard.Formats = [new ClipboardFormatData(13, [1])];
        _injector.ShouldInjectSucceed = false;

        var result = InvokeHook(WM_KEYDOWN, VK_V, flags: 0, extraInfo: 0);

        Assert.Equal((IntPtr)1, result);
        Assert.Equal(1, _injector.Calls);
        Assert.Equal(0, _synthetic.Calls);
    }

    [Fact]
    public void HookCallback_RestrictedTarget_ReadThrows_DoesNotSendSyntheticPaste()
    {
        _keyboard.DownKeys.Add(VK_CONTROL);
        _targetResolver.Resolution = ClipboardPasteTargetResolution.Intercept(new ClipboardPasteTarget((IntPtr)100, 10, 10, 20));
        _clipboard.ThrowOnRead = true;

        var result = InvokeHook(WM_KEYDOWN, VK_V, flags: 0, extraInfo: 0);

        Assert.Equal((IntPtr)1, result);
        Assert.Equal(1, _clipboard.ReadCalls);
        Assert.Equal(0, _synthetic.Calls);
    }

    [Fact]
    public void HookCallback_RestrictedTarget_ScheduleThrows_DoesNotSendSyntheticPaste()
    {
        _keyboard.DownKeys.Add(VK_CONTROL);
        _targetResolver.Resolution = ClipboardPasteTargetResolution.Intercept(new ClipboardPasteTarget((IntPtr)100, 10, 10, 20));
        _workScheduler.ThrowOnRun = true;

        var result = InvokeHook(WM_KEYDOWN, VK_V, flags: 0, extraInfo: 0);

        Assert.Equal((IntPtr)1, result);
        Assert.Equal(1, _workScheduler.RunCalls);
        Assert.Equal(0, _synthetic.Calls);
        Assert.Equal(0, _clipboard.ReadCalls);
    }

    [Fact]
    public void HookCallback_NonRestrictedTarget_PassesThroughAndStartsNoBackgroundWork()
    {
        _keyboard.DownKeys.Add(VK_CONTROL);
        _targetResolver.Resolution = ClipboardPasteTargetResolution.Passthrough();

        var result = InvokeHook(WM_KEYDOWN, VK_V, flags: 0, extraInfo: 0);

        Assert.NotEqual((IntPtr)1, result);
        Assert.Equal(0, _workScheduler.RunCalls);
        Assert.Equal(0, _clipboard.ReadCalls);
        Assert.Equal(0, _injector.Calls);
    }

    [Fact]
    public void HookCallback_InjectionInProgress_SuppressesAndStartsNoBackgroundWork()
    {
        _keyboard.DownKeys.Add(VK_CONTROL);
        _targetResolver.Resolution = ClipboardPasteTargetResolution.Intercept(new ClipboardPasteTarget((IntPtr)100, 10, 10, 20));
        _workScheduler.RunInline = false;
        var firstResult = InvokeHook(WM_KEYDOWN, VK_V, flags: 0, extraInfo: 0);

        var secondResult = InvokeHook(WM_KEYDOWN, VK_V, flags: 0, extraInfo: 0);

        Assert.Equal((IntPtr)1, firstResult);
        Assert.Equal((IntPtr)1, secondResult);
        Assert.Equal(1, _workScheduler.RunCalls);
        Assert.Equal(0, _clipboard.ReadCalls);
        Assert.Equal(0, _injector.Calls);
        Assert.Equal(0, _synthetic.Calls);
    }

    [Fact]
    public void HookCallback_KeyUpAfterIntercept_IsSuppressedOnce()
    {
        _keyboard.DownKeys.Add(VK_CONTROL);
        _targetResolver.Resolution = ClipboardPasteTargetResolution.Intercept(new ClipboardPasteTarget((IntPtr)100, 10, 10, 20));
        _clipboard.Formats = [new ClipboardFormatData(13, [1])];

        InvokeHook(WM_KEYDOWN, VK_V, flags: 0, extraInfo: 0);
        var firstKeyUp = InvokeHook(WM_KEYUP, VK_V, flags: 0, extraInfo: 0);
        var secondKeyUp = InvokeHook(WM_KEYUP, VK_V, flags: 0, extraInfo: 0);

        Assert.Equal((IntPtr)1, firstKeyUp);
        Assert.NotEqual((IntPtr)1, secondKeyUp);
    }

    [Fact]
    public void TargetResolver_NonRestrictedForeground_PassesThrough()
    {
        var restrictedJobInspector = new Mock<IRestrictedJobInspector>();
        var foreground = new Mock<IForegroundWindowResolver>();
        var process = new TestProcessIdentityReader();
        var clipboard = new Mock<IClipboardFormatReader>();
        foreground.Setup(r => r.GetForegroundWindow()).Returns(new ForegroundWindowInfo((IntPtr)10, 100, "Edit"));
        restrictedJobInspector.Setup(i => i.IsProcessInHandleLimitedJob(100)).Returns(false);
        var resolver = new ClipboardPasteTargetResolver(
            foreground.Object,
            process,
            process,
            clipboard.Object,
            restrictedJobInspector.Object,
            _log.Object);

        var result = resolver.Resolve();

        Assert.False(result.ShouldIntercept);
        restrictedJobInspector.Verify(i => i.IsProcessInHandleLimitedJob(100), Times.Once);
    }

    [Fact]
    public void TargetResolver_ConsoleWindow_UsesConhostPid()
    {
        var restrictedJobInspector = new Mock<IRestrictedJobInspector>();
        var foreground = new Mock<IForegroundWindowResolver>();
        var process = new TestProcessIdentityReader();
        var clipboard = new Mock<IClipboardFormatReader>();
        foreground.Setup(r => r.GetForegroundWindow()).Returns(new ForegroundWindowInfo((IntPtr)10, 100, WindowNative.ConsoleWindowClass));
        restrictedJobInspector.Setup(i => i.IsProcessInHandleLimitedJob(100)).Returns(true);
        process.ConsoleHostProcessIds[100] = 200;
        clipboard.Setup(c => c.GetClipboardOwnerWindow()).Returns((IntPtr)20);
        process.WindowProcessIds[(IntPtr)20] = 300;
        var resolver = new ClipboardPasteTargetResolver(
            foreground.Object,
            process,
            process,
            clipboard.Object,
            restrictedJobInspector.Object,
            _log.Object);

        var result = resolver.Resolve();

        Assert.True(result.ShouldIntercept);
        Assert.Equal(100, result.Target.ForegroundProcessId);
        Assert.Equal(200, result.Target.TargetProcessId);
        Assert.Equal(300u, result.Target.ClipboardOwnerProcessId);
        restrictedJobInspector.Verify(i => i.IsProcessInHandleLimitedJob(100), Times.Once);
    }

    [Fact]
    public void TargetResolver_ClipboardOwnerIsTarget_PassesThrough()
    {
        var restrictedJobInspector = new Mock<IRestrictedJobInspector>();
        var foreground = new Mock<IForegroundWindowResolver>();
        var process = new TestProcessIdentityReader();
        var clipboard = new Mock<IClipboardFormatReader>();
        foreground.Setup(r => r.GetForegroundWindow()).Returns(new ForegroundWindowInfo((IntPtr)10, 100, "Edit"));
        restrictedJobInspector.Setup(i => i.IsProcessInHandleLimitedJob(100)).Returns(true);
        clipboard.Setup(c => c.GetClipboardOwnerWindow()).Returns((IntPtr)20);
        process.WindowProcessIds[(IntPtr)20] = 100;
        var resolver = new ClipboardPasteTargetResolver(
            foreground.Object,
            process,
            process,
            clipboard.Object,
            restrictedJobInspector.Object,
            _log.Object);

        var result = resolver.Resolve();

        Assert.False(result.ShouldIntercept);
        restrictedJobInspector.Verify(i => i.IsProcessInHandleLimitedJob(100), Times.Once);
    }

    [Theory]
    [InlineData(2u, false)]
    [InlineData(9u, false)]
    [InlineData(14u, false)]
    [InlineData(0x80u, false)]
    [InlineData(13u, true)]
    [InlineData(15u, true)]
    public void IsGlobalMemoryFormat_ReturnsExpected(uint format, bool expected)
    {
        Assert.Equal(expected, ClipboardFormatReader.IsGlobalMemoryFormat(format));
    }

    [Fact]
    public void BuildDataBlock64_LayoutIsCorrect()
    {
        ClipboardFormatData[] formats = [new(13u, [0x41, 0x00, 0x42, 0x00, 0x00, 0x00])];
        byte[] block = ClipboardPayloadBuilder.BuildDataBlock64(formats, new IntPtr(0x12345678));

        Assert.Equal(0x12345678u, ReadU32(block, 0x40));
        Assert.Equal(0u, ReadU32(block, 0x44));
        Assert.Equal(1u, ReadU32(block, 0x48));
        Assert.Equal(13u, ReadU32(block, 0x4C));
        Assert.Equal(6u, ReadU32(block, 0x50));
        Assert.Equal(0x41, block[0x54]);
    }

    [Fact]
    public void BuildDataBlock32_LayoutIsCorrect()
    {
        ClipboardFormatData[] formats = [new(13u, [0x41, 0x00, 0x00, 0x00])];
        uint[] addrs = [0x11111111u, 0x22222222u, 0x33333333u, 0x44444444u, 0x55555555u, 0x66666666u, 0x77777777u, 0x88888888u];

        byte[] block = ClipboardPayloadBuilder.BuildDataBlock32(formats, addrs, new IntPtr(0x77777778));

        Assert.Equal(0x11111111u, ReadU32(block, 0x00));
        Assert.Equal(0x88888888u, ReadU32(block, 0x1C));
        Assert.Equal(0x77777778u, ReadU32(block, 0x20));
        Assert.Equal(1u, ReadU32(block, 0x24));
        Assert.Equal(13u, ReadU32(block, 0x28));
        Assert.Equal(4u, ReadU32(block, 0x2C));
        Assert.Equal(0x41, block[0x30]);
    }

    private IntPtr InvokeHook(uint msg, int vkCode, uint flags, uint extraInfo)
    {
        var hookStruct = new WindowNative.KBDLLHOOKSTRUCT
        {
            vkCode = (uint)vkCode,
            flags = flags,
            dwExtraInfo = (UIntPtr)extraInfo,
        };

        IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<WindowNative.KBDLLHOOKSTRUCT>());
        try
        {
            Marshal.StructureToPtr(hookStruct, ptr, false);
            return _service.HookCallback(0, (IntPtr)msg, ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static uint ReadU32(byte[] buf, int offset) =>
        (uint)(buf[offset] | (buf[offset + 1] << 8) | (buf[offset + 2] << 16) | (buf[offset + 3] << 24));

    private sealed class FakeKeyboardStateReader : IKeyboardStateReader
    {
        public HashSet<int> DownKeys { get; } = [];
        public bool IsKeyDown(int virtualKey) => DownKeys.Contains(virtualKey);
    }

    private sealed class FakeTargetResolver : IClipboardPasteTargetResolver
    {
        public int ResolveCalls { get; private set; }
        public ClipboardPasteTargetResolution Resolution { get; set; } = ClipboardPasteTargetResolution.Passthrough();
        public ClipboardPasteTargetResolution Resolve()
        {
            ResolveCalls++;
            return Resolution;
        }
    }

    private sealed class FakeClipboardFormatReader : IClipboardFormatReader
    {
        public int ReadCalls { get; private set; }
        public IReadOnlyList<ClipboardFormatData> Formats { get; set; } = [];
        public bool ThrowOnRead { get; set; }
        public IntPtr GetClipboardOwnerWindow() => IntPtr.Zero;
        public IReadOnlyList<ClipboardFormatData> ReadGlobalMemoryFormats()
        {
            ReadCalls++;
            if (ThrowOnRead)
                throw new InvalidOperationException("read failed");
            return Formats;
        }
    }

    private sealed class FakeRemoteProcessInjector : IRemoteProcessInjector
    {
        public int Calls { get; private set; }
        public int LastTargetProcessId { get; private set; }
        public bool ShouldInjectSucceed { get; set; } = true;
        public bool TryInjectClipboardData(int targetProcessId, IntPtr hWnd, IReadOnlyList<ClipboardFormatData> formats)
        {
            Calls++;
            LastTargetProcessId = targetProcessId;
            return ShouldInjectSucceed;
        }
    }

    private sealed class FakeSyntheticInputSender : ISyntheticInputSender
    {
        public int Calls { get; private set; }
        public ClipboardPasteKind LastPasteKind { get; private set; }
        public void SendPaste(ClipboardPasteKind pasteKind)
        {
            Calls++;
            LastPasteKind = pasteKind;
        }
    }

    private sealed class FakeClipboardPasteWorkScheduler : IClipboardPasteWorkScheduler
    {
        public int RunCalls { get; private set; }
        public bool RunInline { get; set; }
        public bool ThrowOnRun { get; set; }

        public void Run(Action action)
        {
            RunCalls++;
            if (ThrowOnRun)
                throw new InvalidOperationException("schedule failed");
            if (RunInline)
                action();
        }
    }

    private sealed class FakeHookApi : ILowLevelHookApi
    {
        public IntPtr InstallKeyboardHook(WindowNative.LowLevelKeyboardProc callback) => (IntPtr)1;
        public IntPtr InstallMouseHook(WindowNative.LowLevelMouseProc callback) => (IntPtr)2;
        public bool Unhook(IntPtr hook) => true;
        public IntPtr CallNextHook(IntPtr hook, int nCode, IntPtr wParam, IntPtr lParam) => (IntPtr)99;
    }

    private sealed class TestProcessIdentityReader : IWindowProcessIdReader, IConsoleHostProcessResolver
    {
        public Dictionary<IntPtr, uint> WindowProcessIds { get; } = [];
        public Dictionary<int, int> ConsoleHostProcessIds { get; } = [];

        public uint GetWindowProcessId(IntPtr hWnd) =>
            WindowProcessIds.TryGetValue(hWnd, out var processId) ? processId : 0;

        public bool TryGetConsoleHostProcessId(int processId, out int consoleHostProcessId) =>
            ConsoleHostProcessIds.TryGetValue(processId, out consoleHostProcessId);
    }
}
