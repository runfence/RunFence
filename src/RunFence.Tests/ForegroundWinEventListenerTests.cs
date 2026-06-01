using System.Collections.Concurrent;
using System.Drawing;
using Moq;
using RunFence.Core;
using RunFence.ForegroundMarker;
using RunFence.Infrastructure;
using Xunit;

namespace RunFence.Tests;

public sealed class ForegroundWinEventListenerTests
{
    [Fact]
    public void ForegroundHook_FiresForCurrentProcessWindow()
    {
        var hookApi = new FakeWinEventHookApi();
        using var listener = CreateListener(
            hookApi,
            processIdByWindow: hwnd => (uint)Environment.ProcessId,
            normalizedWindow: hwnd => hwnd,
            foregroundWindow: (IntPtr)44);
        listener.Start();
        IntPtr? observedWindow = null;
        listener.ForegroundChanged += hwnd => observedWindow = hwnd;

        hookApi.Raise(WindowNative.EventSystemForeground, (IntPtr)44, 0, 0);

        Assert.Equal((IntPtr)44, observedWindow);
    }

    [Fact]
    public void ForegroundHook_TracksCurrentForegroundWindowWhenRawEventWindowDiffers()
    {
        var hookApi = new FakeWinEventHookApi();
        var currentForegroundWindow = (IntPtr)66;
        using var listener = CreateListener(
            hookApi,
            processIdByWindow: _ => 999,
            normalizedWindow: hwnd => hwnd,
            foregroundWindowProvider: () => currentForegroundWindow);
        listener.Start();
        var observed = new List<IntPtr>();
        listener.ForegroundChanged += observed.Add;
        listener.LocationChanged += observed.Add;

        currentForegroundWindow = (IntPtr)10;
        hookApi.Raise(WindowNative.EventSystemForeground, (IntPtr)11, 0, 0);
        hookApi.Raise(0x800B, (IntPtr)10, 0, 0);

        Assert.Equal([(IntPtr)10, (IntPtr)10], observed);
    }

    [Fact]
    public void LocationChange_IgnoresChildCaretAndSelfProcessNoise()
    {
        var hookApi = new FakeWinEventHookApi();
        using var listener = CreateListener(
            hookApi,
            processIdByWindow: hwnd => hwnd == (IntPtr)10 ? (uint)(Environment.ProcessId + 1) : (uint)Environment.ProcessId,
            normalizedWindow: hwnd => hwnd == (IntPtr)11 ? (IntPtr)10 : hwnd);
        listener.Start();
        var observed = new List<IntPtr>();
        listener.LocationChanged += hwnd => observed.Add(hwnd);

        hookApi.Raise(WindowNative.EventSystemForeground, (IntPtr)10, 0, 0);
        hookApi.Raise(0x800B, (IntPtr)10, 0, 0);
        hookApi.Raise(0x800B, (IntPtr)10, 1, 0);
        hookApi.Raise(0x800B, (IntPtr)10, -8, 0);
        hookApi.Raise(0x800B, (IntPtr)11, 0, 0);
        hookApi.Raise(0x800B, (IntPtr)99, 0, 0);

        Assert.Single(observed);
        Assert.Equal((IntPtr)10, observed[0]);
    }

    [Fact]
    public void MoveSizeStart_SuppressesLocationChangesUntilMoveSizeEnd()
    {
        var hookApi = new FakeWinEventHookApi();
        using var listener = CreateListener(hookApi, processIdByWindow: _ => 999, normalizedWindow: hwnd => hwnd);
        listener.Start();
        var events = new List<string>();
        listener.MoveSizeStarted += _ => events.Add("start");
        listener.LocationChanged += _ => events.Add("location");
        listener.MoveSizeEnded += _ => events.Add("end");

        hookApi.Raise(WindowNative.EventSystemForeground, (IntPtr)55, 0, 0);
        hookApi.Raise(WindowNative.EventSystemMoveSizeStart, (IntPtr)55, 0, 0);
        hookApi.Raise(0x800B, (IntPtr)55, 0, 0);
        hookApi.Raise(WindowNative.EventSystemMoveSizeEnd, (IntPtr)55, 0, 0);
        hookApi.Raise(0x800B, (IntPtr)55, 0, 0);

        Assert.Equal(["start", "end", "location"], events);
    }

    [Fact]
    public void Start_TracksCurrentForegroundWindowForMoveSizeEventsBeforeForegroundHookFires()
    {
        var hookApi = new FakeWinEventHookApi();
        using var listener = CreateListener(
            hookApi,
            processIdByWindow: _ => 999,
            normalizedWindow: hwnd => hwnd,
            foregroundWindow: (IntPtr)66);
        var moveSizeStarted = false;
        listener.MoveSizeStarted += _ => moveSizeStarted = true;

        listener.Start();
        hookApi.Raise(WindowNative.EventSystemMoveSizeStart, (IntPtr)66, 0, 0);

        Assert.True(moveSizeStarted);
    }

    [Fact]
    public void Stop_UnhooksAllEventsAndSuppressesLaterCallbacks()
    {
        var hookApi = new FakeWinEventHookApi();
        using var listener = CreateListener(hookApi);
        listener.Start();
        var foregroundCount = 0;
        listener.ForegroundChanged += _ => foregroundCount++;

        listener.Stop();
        hookApi.Raise(WindowNative.EventSystemForeground, (IntPtr)77, 0, 0);

        Assert.Equal(4, hookApi.UnhookedHandles.Count);
        Assert.Equal(0, foregroundCount);
        Assert.False(listener.IsStarted);
    }

    [Fact]
    public void Stop_ClearsTrackedForegroundWindowState()
    {
        var hookApi = new FakeWinEventHookApi();
        using var listener = CreateListener(hookApi, processIdByWindow: _ => 999, normalizedWindow: hwnd => hwnd);
        listener.Start();
        var locationChanged = false;

        hookApi.Raise(WindowNative.EventSystemForeground, (IntPtr)88, 0, 0);
        listener.Stop();
        listener.Start();
        listener.LocationChanged += _ => locationChanged = true;

        hookApi.Raise(0x800B, (IntPtr)88, 0, 0);

        Assert.False(locationChanged);
    }

    [Fact]
    public void Start_WhenLaterHookInstallFails_UnhooksEarlierHooksAndThrows()
    {
        var hookApi = new FakeWinEventHookApi
        {
            EventToFail = WindowNative.EventSystemMoveSizeEnd
        };
        using var listener = CreateListener(hookApi);

        var exception = Assert.Throws<InvalidOperationException>(() => listener.Start());

        Assert.Contains("0x000B", exception.Message);
        Assert.Equal(2, hookApi.UnhookedHandles.Count);
        Assert.False(listener.IsStarted);
    }

    private static ForegroundWinEventListener CreateListener(
        FakeWinEventHookApi hookApi,
        Func<IntPtr, uint>? processIdByWindow = null,
        Func<IntPtr, string>? classNameByWindow = null,
        Func<IntPtr, IntPtr>? normalizedWindow = null,
        IntPtr? foregroundWindow = null,
        Func<IntPtr>? foregroundWindowProvider = null)
    {
        processIdByWindow ??= _ => (uint)(Environment.ProcessId + 1);
        classNameByWindow ??= _ => "TestWindow";
        normalizedWindow ??= hwnd => hwnd;
        foregroundWindowProvider ??= () => foregroundWindow ?? IntPtr.Zero;

        var resolver = new Mock<IForegroundWindowResolver>();
        resolver.Setup(r => r.GetForegroundWindow())
            .Returns(() =>
            {
                var hwnd = foregroundWindowProvider();
                return new ForegroundWindowInfo(hwnd, processIdByWindow(hwnd), classNameByWindow(hwnd));
            });
        resolver.Setup(r => r.GetWindowInfo(It.IsAny<IntPtr>()))
            .Returns((IntPtr hwnd) => new ForegroundWindowInfo(hwnd, processIdByWindow(hwnd), classNameByWindow(hwnd)));

        var boundsReader = new ForegroundWindowBoundsReader(
            Mock.Of<IWindowFrameBoundsReader>(),
            new FakeForegroundMarkerNativeMethods(normalizedWindow),
            new FakeMonitorIntersectionService());

        return new ForegroundWinEventListener(resolver.Object, boundsReader, hookApi, Mock.Of<ILoggingService>());
    }

    private sealed class FakeWinEventHookApi : IWinEventHookApi
    {
        private long nextHandleValue = 1;
        private readonly ConcurrentDictionary<IntPtr, Registration> activeRegistrations = new();
        public uint? EventToFail { get; init; }

        public List<Registration> Registrations { get; } = [];
        public List<IntPtr> UnhookedHandles { get; } = [];

        public IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            IntPtr hmodWinEventProc,
            WindowNative.WinEventDelegate callback,
            uint processId,
            uint threadId,
            uint flags)
        {
            if (EventToFail == eventMin)
                return IntPtr.Zero;

            var handle = (IntPtr)Interlocked.Increment(ref nextHandleValue);
            var registration = new Registration(handle, eventMin, eventMax, callback, flags);
            Registrations.Add(registration);
            activeRegistrations[handle] = registration;
            return handle;
        }

        public bool UnhookWinEvent(IntPtr hookHandle)
        {
            UnhookedHandles.Add(hookHandle);
            return activeRegistrations.TryRemove(hookHandle, out _);
        }

        public void Raise(uint eventType, IntPtr hwnd, int idObject, int idChild)
        {
            foreach (var registration in activeRegistrations.Values.Where(r => r.EventMin == eventType && r.EventMax == eventType))
                registration.Callback(registration.Handle, eventType, hwnd, idObject, idChild, 0, 0);
        }
    }

    private sealed record Registration(
        IntPtr Handle,
        uint EventMin,
        uint EventMax,
        WindowNative.WinEventDelegate Callback,
        uint Flags);

    private sealed class FakeMonitorIntersectionService : IForegroundMonitorIntersectionService
    {
        public bool TryGetMonitorBounds(Rectangle bounds, out Rectangle monitorBounds)
        {
            monitorBounds = bounds;
            return true;
        }
    }

    private sealed class FakeForegroundMarkerNativeMethods : IForegroundMarkerNativeMethods
    {
        private readonly Func<IntPtr, IntPtr> normalizedWindow;

        public FakeForegroundMarkerNativeMethods(Func<IntPtr, IntPtr> normalizedWindow)
        {
            this.normalizedWindow = normalizedWindow;
        }

        public IntPtr GetAncestor(IntPtr hwnd, uint gaFlags) => normalizedWindow(hwnd);
        public IntPtr GetPreviousWindow(IntPtr hwnd) => IntPtr.Zero;
        public IntPtr TopmostInsertAfter => new(-1);
        public IntPtr NotTopmostInsertAfter => new(-2);
        public bool IsWindow(IntPtr hwnd) => hwnd != IntPtr.Zero;
        public bool IsIconic(IntPtr hwnd) => false;
        public bool TryGetWindowStyle(IntPtr hwnd, out long windowStyle)
        {
            windowStyle = 0;
            return true;
        }

        public bool TryGetWindowExStyle(IntPtr hwnd, out long windowExStyle)
        {
            windowExStyle = 0;
            return true;
        }

        public bool TryGetWindowRect(IntPtr hwnd, out Rectangle bounds)
        {
            bounds = new Rectangle(10, 10, 100, 100);
            return true;
        }

        public bool TryGetWindowCloaked(IntPtr hwnd, out bool isCloaked)
        {
            isCloaked = false;
            return true;
        }

        public bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags) => true;
        public bool ShowWindow(IntPtr hwnd, int command) => true;
        public bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase) => true;
        public bool SetLayeredWindowAttributes(IntPtr hwnd, uint colorKey, byte alpha, uint flags) => true;
    }
}
