using RunFence.Infrastructure;
using RunFence.Core;

namespace RunFence.ForegroundMarker;

public sealed class ForegroundWinEventListener : IForegroundWinEventListener
{
    private const uint EventObjectLocationChange = 0x800B;
    private const int ObjidWindow = 0;
    private const int ChildIdSelf = 0;
    private readonly IForegroundWindowResolver foregroundWindowResolver;
    private readonly IForegroundWindowBoundsReader boundsReader;
    private readonly IWinEventHookApi hookApi;
    private readonly ILoggingService log;
    private readonly WindowNative.WinEventDelegate callback;
    private IntPtr foregroundHook;
    private IntPtr moveSizeStartHook;
    private IntPtr moveSizeEndHook;
    private IntPtr locationChangeHook;
    private IntPtr trackedForegroundWindow;
    private bool moveSizeActive;
    private bool disposed;

    public ForegroundWinEventListener(
        IForegroundWindowResolver foregroundWindowResolver,
        IForegroundWindowBoundsReader boundsReader,
        IWinEventHookApi hookApi,
        ILoggingService log)
    {
        this.foregroundWindowResolver = foregroundWindowResolver;
        this.boundsReader = boundsReader;
        this.hookApi = hookApi;
        this.log = log;
        callback = OnWinEvent;
    }

    public event Action<IntPtr>? ForegroundChanged;
    public event Action<IntPtr>? MoveSizeStarted;
    public event Action<IntPtr>? MoveSizeEnded;
    public event Action<IntPtr>? LocationChanged;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (foregroundHook != IntPtr.Zero)
            return;

        try
        {
            foregroundHook = InstallHook(WindowNative.EventSystemForeground);
            moveSizeStartHook = InstallHook(WindowNative.EventSystemMoveSizeStart);
            moveSizeEndHook = InstallHook(WindowNative.EventSystemMoveSizeEnd);
            locationChangeHook = InstallHook(EventObjectLocationChange);
            trackedForegroundWindow = boundsReader.ResolveTrackedTopLevelWindow(
                foregroundWindowResolver.GetForegroundWindow().HWnd);
            moveSizeActive = false;
            log.Debug($"ForegroundWinEventListener: started; tracked foreground hwnd=0x{trackedForegroundWindow.ToInt64():X}.");
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        Unhook(ref foregroundHook);
        Unhook(ref moveSizeStartHook);
        Unhook(ref moveSizeEndHook);
        Unhook(ref locationChangeHook);
        trackedForegroundWindow = IntPtr.Zero;
        moveSizeActive = false;
    }

    public void Dispose()
    {
        if (disposed)
            return;

        Stop();
        disposed = true;
    }

    public bool IsStarted =>
        foregroundHook != IntPtr.Zero
        && moveSizeStartHook != IntPtr.Zero
        && moveSizeEndHook != IntPtr.Zero
        && locationChangeHook != IntPtr.Zero;

    private IntPtr InstallHook(uint eventId)
    {
        var hookHandle = hookApi.SetWinEventHook(
            eventId,
            eventId,
            IntPtr.Zero,
            callback,
            0,
            0,
            WindowNative.WinEventOutOfContext);
        if (hookHandle == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to install WinEvent hook 0x{eventId:X4}.");

        return hookHandle;
    }

    private void OnWinEvent(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        switch (eventType)
        {
            case WindowNative.EventSystemForeground:
                trackedForegroundWindow = ResolveCurrentForegroundWindow(hwnd);
                moveSizeActive = false;
                ForegroundChanged?.Invoke(trackedForegroundWindow);
                break;
            case WindowNative.EventSystemMoveSizeStart:
                if (TryGetTrackedForegroundTarget(hwnd, idObject, idChild, requireTopLevelSource: true, out var moveSizeStartTarget))
                {
                    moveSizeActive = true;
                    MoveSizeStarted?.Invoke(moveSizeStartTarget);
                }
                break;
            case WindowNative.EventSystemMoveSizeEnd:
                if (TryGetTrackedForegroundTarget(hwnd, idObject, idChild, requireTopLevelSource: true, out var moveSizeEndTarget))
                {
                    moveSizeActive = false;
                    MoveSizeEnded?.Invoke(moveSizeEndTarget);
                }
                break;
            case EventObjectLocationChange:
                if (moveSizeActive)
                    return;

                if (TryGetTrackedForegroundTarget(hwnd, idObject, idChild, requireTopLevelSource: true, out var locationTarget)
                    && !BelongsToCurrentProcess(locationTarget))
                {
                    LocationChanged?.Invoke(locationTarget);
                }
                break;
        }
    }

    private bool TryGetTrackedForegroundTarget(
        IntPtr hwnd,
        int idObject,
        int idChild,
        bool requireTopLevelSource,
        out IntPtr trackedTarget)
    {
        trackedTarget = IntPtr.Zero;
        if (hwnd == IntPtr.Zero || idObject != ObjidWindow || idChild != ChildIdSelf)
            return false;

        if (trackedForegroundWindow == IntPtr.Zero)
            return false;

        var resolvedWindow = boundsReader.ResolveTrackedTopLevelWindow(hwnd);
        if (resolvedWindow == IntPtr.Zero || resolvedWindow != trackedForegroundWindow)
            return false;

        if (requireTopLevelSource && resolvedWindow != hwnd)
            return false;

        trackedTarget = resolvedWindow;
        return true;
    }

    private IntPtr ResolveCurrentForegroundWindow(IntPtr eventWindow)
    {
        var currentForegroundWindow = foregroundWindowResolver.GetForegroundWindow().HWnd;
        var resolvedCurrentWindow = boundsReader.ResolveTrackedTopLevelWindow(currentForegroundWindow);
        return resolvedCurrentWindow != IntPtr.Zero
            ? resolvedCurrentWindow
            : boundsReader.ResolveTrackedTopLevelWindow(eventWindow);
    }

    private bool BelongsToCurrentProcess(IntPtr hwnd)
    {
        var info = foregroundWindowResolver.GetWindowInfo(hwnd);
        return info.ProcessId == (uint)Environment.ProcessId;
    }

    private void Unhook(ref IntPtr hookHandle)
    {
        if (hookHandle == IntPtr.Zero)
            return;

        _ = hookApi.UnhookWinEvent(hookHandle);
        hookHandle = IntPtr.Zero;
    }
}
