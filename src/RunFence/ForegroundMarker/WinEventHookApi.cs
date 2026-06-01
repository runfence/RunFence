using RunFence.Infrastructure;

namespace RunFence.ForegroundMarker;

public interface IWinEventHookApi
{
    IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WindowNative.WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    bool UnhookWinEvent(IntPtr hookHandle);
}

internal sealed class WinEventHookApi : IWinEventHookApi
{
    public IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr hmodWinEventProc,
        WindowNative.WinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags)
        => WindowNative.SetWinEventHook(eventMin, eventMax, hmodWinEventProc, callback, processId, threadId, flags);

    public bool UnhookWinEvent(IntPtr hookHandle) => WindowNative.UnhookWinEvent(hookHandle);
}
